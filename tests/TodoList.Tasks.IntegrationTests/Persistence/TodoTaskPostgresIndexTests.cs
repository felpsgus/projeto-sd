using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// CA-20 de BE-05: prova, contra o catálogo de um Postgres real (não o
/// modelo do EF em memória — isso já é <c>PersistenceModelTests</c>, sem
/// Docker), que a migration gerada por <c>dotnet ef migrations add</c>
/// realmente cria os índices <c>(owner_id, status)</c> e
/// <c>(owner_id, due_date)</c> em <c>tasks.tasks</c>. <b>Requer Docker.</b>
/// Mesmo padrão de <see cref="PostgresPersistenceTests"/> (BE-02): container
/// compartilhado da coleção "Postgres", só iniciado dentro do corpo do teste.
/// </summary>
[Collection("Postgres")]
public class TodoTaskPostgresIndexTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;

    public TodoTaskPostgresIndexTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        // Migrations reais (as mesmas versionadas em Migrations/), não
        // EnsureCreated: é a migration em si que está sob teste (CA-20).
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();

        // Ordem obrigatória (README, nota técnica de BE-02, BE-02 CA-02c): a
        // migration do Tasks que cria a FK cruzada referencia identity.users,
        // então o Identity precisa estar migrado antes do Tasks.
        await using (var identityContext = CreateIdentityContext())
        {
            await identityContext.Database.MigrateAsync();
        }

        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task Migration_AplicadaEmPostgresReal_CriaIndiceOwnerIdStatus()
    {
        var colunas = await LerColunasDoIndiceAsync("ix_tasks_owner_id_status");

        colunas.Should().Equal(["owner_id", "status"]);
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task Migration_AplicadaEmPostgresReal_CriaIndiceOwnerIdDueDate()
    {
        var colunas = await LerColunasDoIndiceAsync("ix_tasks_owner_id_due_date");

        colunas.Should().Equal(["owner_id", "due_date"]);
    }

    private async Task<List<string>> LerColunasDoIndiceAsync(string indexName)
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.attname
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'tasks' AND c.relname = @indexName
            ORDER BY array_position(i.indkey, a.attnum);
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "indexName";
        parameter.Value = indexName;
        command.Parameters.Add(parameter);

        var colunas = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            colunas.Add(reader.GetString(0));
        }

        return colunas;
    }

    private TasksDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            // Mesma convenção usada em runtime (ServiceCollectionExtensions) e
            // pelo design-time factory (TasksDbContextFactory) — sem isso o
            // modelo construído aqui diverge do snapshot das migrations
            // (nomes de coluna) e o EF acusa "pending model changes" ao
            // migrar, mesmo com a migration já correta no disco.
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
