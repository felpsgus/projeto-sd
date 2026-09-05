using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// FK cruzada entre schemas (BE-02 CA-02c/CA-15/CA-16, D-27):
/// <c>tasks.tasks.owner_id → identity.users(id) ON DELETE CASCADE</c>. É a
/// mesma "rede de segurança" que sustenta a exclusão de conta em cascata de
/// <see href="../../../tarefas/backend/BE-16-exclusao-conta.md">BE-16</see>.
/// <b>Requer Docker</b> (<c>Category=Docker</c>) — mesmo padrão de
/// <see cref="PostgresPersistenceTests"/>.
///
/// <para>
/// Referencia <c>TodoList.Identity.Infrastructure</c> só para <b>montar dados
/// de teste</b> (inserir/apagar usuário via <see cref="IdentityDbContext"/>) —
/// isso não contradiz D-27/CA-13: o <see cref="TasksDbContext"/> em si
/// continua sem mapear <c>User</c>, a FK em si é SQL puro na migration, e o
/// código de produção do Tasks nunca referencia o Identity por aqui (só o
/// gRPC declarado em BE-27/BE-28).
/// </para>
///
/// <para>
/// <see cref="InitializeAsync"/> aplica as migrations do <b>Identity antes</b>
/// das do Tasks — é a ordem obrigatória documentada no README: a migration do
/// Tasks que cria a FK referencia <c>identity.users</c>, então precisa que o
/// schema/tabela já exista.
/// </para>
/// </summary>
[Collection("Postgres")]
public class CrossSchemaForeignKeyTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private FakeTimeProvider _timeProvider = null!;

    public CrossSchemaForeignKeyTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await _fixture.EnsureStartedAsync();

        // Banco zerado (CA-10: independência entre testes) e, a partir daí,
        // Identity primeiro, Tasks depois — a ordem que a migration da FK exige.
        await using (var wipe = CreateTasksContext())
        {
            await wipe.Database.EnsureDeletedAsync();
        }

        await using (var identityContext = CreateIdentityContext())
        {
            await identityContext.Database.MigrateAsync();
        }

        await using (var tasksContext = CreateTasksContext())
        {
            await tasksContext.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task Catalogo_TemFkCruzadaDeTasksParaIdentityUsersComOnDeleteCascade() // CA-02c
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                con.conname,
                con.confdeltype::text AS confdeltype,
                src_ns.nspname AS src_schema,
                src_tbl.relname AS src_table,
                src_col.attname AS src_column,
                tgt_ns.nspname AS tgt_schema,
                tgt_tbl.relname AS tgt_table,
                tgt_col.attname AS tgt_column
            FROM pg_constraint con
            JOIN pg_class src_tbl ON src_tbl.oid = con.conrelid
            JOIN pg_namespace src_ns ON src_ns.oid = src_tbl.relnamespace
            JOIN pg_class tgt_tbl ON tgt_tbl.oid = con.confrelid
            JOIN pg_namespace tgt_ns ON tgt_ns.oid = tgt_tbl.relnamespace
            JOIN pg_attribute src_col ON src_col.attrelid = con.conrelid AND src_col.attnum = con.conkey[1]
            JOIN pg_attribute tgt_col ON tgt_col.attrelid = con.confrelid AND tgt_col.attnum = con.confkey[1]
            WHERE con.contype = 'f' AND src_ns.nspname = 'tasks' AND src_tbl.relname = 'tasks';
            """;

        await using var reader = await command.ExecuteReaderAsync();

        var encontrouLinha = await reader.ReadAsync();
        encontrouLinha.Should().BeTrue(
            "o catálogo do Postgres precisa ter uma FK saindo de tasks.tasks — sem ela, CA-02c não está satisfeito");

        reader.GetString(reader.GetOrdinal("conname")).Should().Be("fk_tasks_owner_id_identity_users");
        reader.GetString(reader.GetOrdinal("confdeltype")).Should().Be(
            "c", "'c' é o código do catálogo para ON DELETE CASCADE (CA-02c)");
        reader.GetString(reader.GetOrdinal("src_schema")).Should().Be("tasks");
        reader.GetString(reader.GetOrdinal("src_table")).Should().Be("tasks");
        reader.GetString(reader.GetOrdinal("src_column")).Should().Be("owner_id");
        reader.GetString(reader.GetOrdinal("tgt_schema")).Should().Be("identity");
        reader.GetString(reader.GetOrdinal("tgt_table")).Should().Be("users");
        reader.GetString(reader.GetOrdinal("tgt_column")).Should().Be("id");

        (await reader.ReadAsync()).Should().BeFalse("só existe uma FK cruzada — CA-02c não fala de nenhuma outra");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SaveChangesAsync_ComOwnerIdInexistenteEmIdentityUsers_EhRejeitadoComSqlState23503() // CA-15
    {
        var tarefaOrfa = TodoTask.Create(Guid.NewGuid(), "tarefa sem dono em identity.users", null, null, null, _timeProvider).Value;

        await using var context = CreateTasksContext();
        context.Tasks.Add(tarefaOrfa);

        var inserindo = async () => await context.SaveChangesAsync();

        var excecao = await inserindo.Should().ThrowAsync<DbUpdateException>(
            "um owner_id sem linha correspondente em identity.users precisa ser rejeitado pelo banco — é a rede "
            + "de segurança da FK (CA-15); o caminho normal de rejeição é BE-28, não este");

        excecao.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                PostgresErrorCodes.ForeignKeyViolation,
                "23503 é o SQLSTATE de violação de chave estrangeira — a prova de que a FK está ativa (CA-15)");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task DeleteUsuario_RemoveEmCascataAsTarefasDoDono_InclusiveAsSoftDeleted() // CA-16
    {
        var email = Email.Create($"cascade-{Guid.NewGuid():N}@example.com").Value;
        var dono = User.Create(email, "Dono da Cascata", "hash-placeholder-de-teste", _timeProvider).Value;

        await using (var identityContext = CreateIdentityContext())
        {
            identityContext.Users.Add(dono);
            await identityContext.SaveChangesAsync();
        }

        var tarefaAtiva = TodoTask.Create(dono.Id, "tarefa ativa do dono", null, null, null, _timeProvider).Value;
        var tarefaSoftDeleted = TodoTask.Create(dono.Id, "tarefa soft-deleted do dono", null, null, null, _timeProvider).Value;
        tarefaSoftDeleted.SoftDelete(_timeProvider).IsSuccess.Should().BeTrue();

        await using (var tasksContext = CreateTasksContext())
        {
            tasksContext.Tasks.AddRange(tarefaAtiva, tarefaSoftDeleted);
            await tasksContext.SaveChangesAsync();
        }

        await using (var identityContext = CreateIdentityContext())
        {
            identityContext.Users.Remove(dono);
            await identityContext.SaveChangesAsync();
        }

        // IgnoreQueryFilters(): senão o filtro global de soft delete esconderia
        // justamente a tarefa que precisava ter sumido pela cascata do banco —
        // o caso que este teste existe para provar (CA-16, nota técnica de BE-02).
        await using var contextoDeVerificacao = CreateTasksContext();
        var remanescentes = await contextoDeVerificacao.Tasks
            .IgnoreQueryFilters()
            .Where(tarefa => tarefa.OwnerId == dono.Id)
            .ToListAsync();

        remanescentes.Should().BeEmpty(
            "apagar o usuário precisa remover em cascata todas as tarefas dele no banco, inclusive as "
            + "soft-deleted, sem nenhum registro órfão (CA-16) — é o que sustenta BE-16");
    }

    private TasksDbContext CreateTasksContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TasksDbContext(options);
    }

    private IdentityDbContext CreateIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(IdentityDbContext.MigrationsHistoryTableName, IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options);
    }
}
