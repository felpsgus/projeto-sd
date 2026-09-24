using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// BE-41, CA-11 — prova, contra Postgres real (Testcontainers, não o SQLite
/// in-memory de <c>ListTasksGrpcTests</c>), que
/// <see cref="TodoTaskRepository.ListByOwnerAsync"/> executa filtro,
/// ordenação e paginação **no banco**: o SQL efetivamente enviado ao servidor
/// contém <c>WHERE</c>, <c>ORDER BY</c> e <c>LIMIT</c>/<c>OFFSET</c> — nunca
/// avaliação em memória. <b>Requer Docker.</b> Mesmo padrão de
/// <see cref="PostgresPersistenceTests"/>/<see cref="TodoTaskPostgresIndexTests"/>
/// (BE-02, BE-05): container compartilhado da coleção "Postgres".
/// </summary>
[Collection("Postgres")]
public class ListTasksPostgresQueryTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private FakeTimeProvider _timeProvider = null!;

    public ListTasksPostgresQueryTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await _fixture.EnsureStartedAsync();

        // Sem FK cruzada de schema no modelo do EF (ver TodoTaskConfiguration
        // — a FK real é SQL explícito de migration, fora do modelo), então
        // EnsureCreated sobre só o TasksDbContext basta aqui, mesmo padrão de
        // PostgresPersistenceTests (ProbeDbContext).
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task ListByOwnerAsync_ContraPostgresReal_GeraSqlComWhereOrderByELimitOffset()
    {
        var owner = Guid.NewGuid();

        await using (var seedContext = CreateContext())
        {
            for (var i = 0; i < 3; i++)
            {
                seedContext.Tasks.Add(TodoTask.Create(owner, $"Tarefa {i}", null, null, null, _timeProvider).Value);
                _timeProvider.Advance(TimeSpan.FromSeconds(1));
            }

            await seedContext.SaveChangesAsync();
        }

        var comandosSql = new List<string>();
        await using var context = CreateContext(sql => comandosSql.Add(sql));
        var repository = new TodoTaskRepository(context);
        var filter = new TaskListFilter(TaskStatusFilter.All, [], null, null, new DateOnly(2026, 1, 1));

        var (items, totalCount) = await repository.ListByOwnerAsync(owner, page: 1, pageSize: 2, filter);

        items.Should().HaveCount(2, "a página pedida não deve materializar mais linhas que pageSize");
        totalCount.Should().Be(3, "total_count é o total do dono, não o total da página");

        var textoCompleto = string.Join('\n', comandosSql);
        textoCompleto.Should().Contain("WHERE", "o filtro por dono precisa ser traduzido para SQL, não avaliado em memória");
        textoCompleto.Should().Contain("ORDER BY", "a ordenação precisa acontecer no banco");
        textoCompleto.Should().MatchRegex("LIMIT|OFFSET", "a paginação precisa acontecer no banco, via LIMIT/OFFSET (dialeto Postgres)");
    }

    [Fact] // BE-22, CA-32/CA-33 — filtro de status/prioridade, busca e overdue viram parte do WHERE
    [Trait("Category", "Docker")]
    public async Task ListByOwnerAsync_ComFiltrosBuscaEOverdue_ComponemOMesmoWhereNoSql()
    {
        var owner = Guid.NewGuid();
        var today = new DateOnly(2026, 1, 10);

        await using (var seedContext = CreateContext())
        {
            var vencida = TodoTask.Create(owner, "Relatório mensal", null, TaskPriority.High, new DateOnly(2026, 1, 5), _timeProvider).Value;
            seedContext.Tasks.Add(vencida);
            await seedContext.SaveChangesAsync();
        }

        var comandosSql = new List<string>();
        await using var context = CreateContext(sql => comandosSql.Add(sql));
        var repository = new TodoTaskRepository(context);
        var filter = new TaskListFilter(
            TaskStatusFilter.Pending,
            [TaskPriority.High, TaskPriority.Low],
            Overdue: true,
            Search: "relat",
            Today: today);

        var (items, totalCount) = await repository.ListByOwnerAsync(owner, page: 1, pageSize: 20, filter);

        items.Should().ContainSingle();
        totalCount.Should().Be(1);

        var textoCompleto = string.Join('\n', comandosSql);
        textoCompleto.Should().Contain("WHERE", "os filtros combinados precisam estar no WHERE, não avaliados em memória");
        textoCompleto.Should().MatchRegex(
            "(?i)like",
            "a busca textual precisa ser traduzida para LIKE/ILIKE no SQL, nunca comparada em memória");

        // CA-33: overdue é derivado (status=pending AND due_date < hoje), nunca
        // uma coluna própria — a prova de que não foi avaliado em memória é o
        // texto "due_date" (ou a coluna equivalente) aparecer dentro do SQL
        // capturado, ao lado do parâmetro de data.
        textoCompleto.Should().MatchRegex("(?i)due_date", "o filtro overdue precisa referenciar due_date dentro do SQL enviado ao banco");
    }

    private TasksDbContext CreateContext(Action<string>? onCommandLogged = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditingSaveChangesInterceptor(_timeProvider));

        if (onCommandLogged is not null)
        {
            // CA-11: captura o texto do comando SQL efetivamente enviado ao
            // Postgres — não uma inspeção do IQueryable em memória.
            optionsBuilder.LogTo(onCommandLogged, LogLevel.Information);
        }

        return new TasksDbContext(optionsBuilder.Options);
    }
}
