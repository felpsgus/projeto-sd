using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TodoList.SharedKernel.Persistence;
using TodoList.SharedKernel.Retention;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using TodoList.Tasks.Infrastructure.Retention;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// BE-23 contra Postgres real (<b>requer Docker</b>), com o purger chamado direto — sem hospedar o worker.
/// O relógio é um <see cref="FakeTimeProvider"/>: "removida há N dias" é escrito recuando o relógio.
/// </summary>
[Collection("Postgres")]
public class TasksRetentionPurgerTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly DateTimeOffset _now;

    public TasksRetentionPurgerTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        _now = _time.GetUtcNow();
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact] // CA-01, CA-02, CA-03
    [Trait("Category", "Docker")]
    public async Task Purge_ApagaSoOQuePassouDaRetencao_ENuncaTarefaAtiva()
    {
        var owner = Guid.NewGuid();
        var velha = await AddAsync(owner, deletedDaysAgo: 31);
        var recente = await AddAsync(owner, deletedDaysAgo: 29);
        var ativaAntiga = await AddAsync(owner, deletedDaysAgo: null, createdDaysAgo: 400);

        var result = await PurgeAsync();

        result["tasks"].Should().Be(1);
        (await ExistingIdsAsync()).Should().BeEquivalentTo([recente, ativaAntiga]).And.NotContain(velha);
    }

    [Fact] // CA-04
    [Trait("Category", "Docker")]
    public async Task Purge_RetencaoDeUmDia_AlcancaTarefaRemovidaOntem()
    {
        var ontem = await AddAsync(Guid.NewGuid(), deletedDaysAgo: 2);

        (await PurgeAsync(retentionDays: 30))["tasks"].Should().Be(0);
        (await PurgeAsync(retentionDays: 1))["tasks"].Should().Be(1);
        (await ExistingIdsAsync()).Should().NotContain(ontem);
    }

    [Fact] // CA-13
    [Trait("Category", "Docker")]
    public async Task Purge_ApagaDeTodosOsDonosSemTocarNasAtivasDeNinguem()
    {
        var dono1 = Guid.NewGuid();
        var dono2 = Guid.NewGuid();
        await AddAsync(dono1, deletedDaysAgo: 40);
        var ativaDono1 = await AddAsync(dono1, deletedDaysAgo: null);
        var recenteDono2 = await AddAsync(dono2, deletedDaysAgo: 5);

        await PurgeAsync();

        (await ExistingIdsAsync()).Should().BeEquivalentTo([ativaDono1, recenteDono2]);
    }

    [Fact] // CA-07, CA-12
    [Trait("Category", "Docker")]
    public async Task Purge_ComMaisRegistrosQueOLote_EsvaziaOBacklogEASegundaExecucaoNaoRemoveNada()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
        {
            await AddAsync(owner, deletedDaysAgo: 45 + i);
        }

        var survivor = await AddAsync(owner, deletedDaysAgo: 3);

        (await PurgeAsync(batchSize: 10))["tasks"].Should().Be(25);
        (await PurgeAsync(batchSize: 10))["tasks"].Should().Be(0);
        (await ExistingIdsAsync()).Should().BeEquivalentTo([survivor]);
    }

    private async Task<IReadOnlyDictionary<string, int>> PurgeAsync(int retentionDays = 30, int batchSize = 500)
    {
        await using var context = CreateContext();
        var purger = new TasksRetentionPurger(
            context,
            _time,
            Options.Create(new TaskOptions { SoftDeleteRetentionDays = retentionDays }),
            Options.Create(new RetentionOptions { BatchSize = batchSize }));

        return await purger.PurgeAsync(CancellationToken.None);
    }

    private async Task<Guid> AddAsync(Guid owner, int? deletedDaysAgo, int createdDaysAgo = 0)
    {
        await using var context = CreateContext();

        var clock = new FakeTimeProvider(_now.AddDays(-createdDaysAgo));
        var task = TodoTask.Create(owner, "Tarefa", null, null, null, clock).Value;

        if (deletedDaysAgo is not null)
        {
            task.SoftDelete(new FakeTimeProvider(_now.AddDays(-deletedDaysAgo.Value)));
        }

        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task<List<Guid>> ExistingIdsAsync()
    {
        await using var context = CreateContext();

        return await context.Tasks.IgnoreQueryFilters().Select(task => task.Id).ToListAsync();
    }

    private TasksDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditingSaveChangesInterceptor(_time))
            .Options);
}
