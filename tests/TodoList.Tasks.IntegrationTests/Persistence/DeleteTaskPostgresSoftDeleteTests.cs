using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.SharedKernel.Persistence;
using TodoList.Tasks.Domain.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Persistence;

/// <summary>
/// BE-21, contra Postgres real (Testcontainers, não o SQLite in-memory de
/// <c>DeleteTaskGrpcTests</c>) — prova, ao nível do repositório, que o soft
/// delete (D-07) preserva a linha física (CA-02: <c>DeletedAt</c> só visível
/// via <c>IgnoreQueryFilters()</c>) e libera a vaga no limite de tarefas
/// ativas (CA-06/RN-TASK-15) — a contagem usada pelo limite de BE-17
/// (<c>CountActiveByOwnerAsync</c>) roda contra o banco real, não em memória.
/// <b>Requer Docker.</b> Mesmo padrão de <see cref="ListTasksPostgresQueryTests"/>.
/// </summary>
[Collection("Postgres")]
public class DeleteTaskPostgresSoftDeleteTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private FakeTimeProvider _timeProvider = null!;

    public DeleteTaskPostgresSoftDeleteTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        await _fixture.EnsureStartedAsync();

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SoftDelete_ContraPostgresReal_PreservaALinhaComDeletedAtPreenchido()
    {
        var owner = Guid.NewGuid();
        Guid taskId;

        await using (var context = CreateContext())
        {
            var task = TodoTask.Create(owner, "Tarefa", null, null, null, _timeProvider).Value;
            context.Tasks.Add(task);
            await context.SaveChangesAsync();
            taskId = task.Id;
        }

        await using (var context = CreateContext())
        {
            var repository = new TodoTaskRepository(context);
            var task = await repository.GetOwnedTaskAsync(owner, taskId);
            task!.SoftDelete(_timeProvider);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var naConsultaNormal = await context.Tasks.SingleOrDefaultAsync(t => t.Id == taskId);
            naConsultaNormal.Should().BeNull("uma tarefa removida não aparece na consulta normal (filtro global de BE-02)");

            var comFiltroIgnorado = await context.Tasks.IgnoreQueryFilters().SingleOrDefaultAsync(t => t.Id == taskId);
            comFiltroIgnorado.Should().NotBeNull("a linha continua no banco (CA-02, D-07) — IgnoreQueryFilters() é o caminho explícito para vê-la");
            comFiltroIgnorado!.DeletedAt.Should().NotBeNull();
        }
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task SoftDelete_ContraPostgresReal_LiberaVagaNoLimiteDeTarefasAtivas()
    {
        var owner = Guid.NewGuid();
        Guid taskId;

        await using (var context = CreateContext())
        {
            var repository = new TodoTaskRepository(context);
            var task = TodoTask.Create(owner, "Única tarefa ativa", null, null, null, _timeProvider).Value;
            repository.Add(task);
            await context.SaveChangesAsync();
            taskId = task.Id;

            (await repository.CountActiveByOwnerAsync(owner)).Should().Be(1);
        }

        await using (var context = CreateContext())
        {
            var repository = new TodoTaskRepository(context);
            var task = await repository.GetOwnedTaskAsync(owner, taskId);
            task!.SoftDelete(_timeProvider);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var repository = new TodoTaskRepository(context);
            (await repository.CountActiveByOwnerAsync(owner)).Should().Be(
                0, "a tarefa removida deixa de contar como ativa (RN-TASK-15), liberando a vaga para uma nova criação (BE-21, CA-06)");
        }
    }

    private TasksDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                _fixture.ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable(TasksDbContext.MigrationsHistoryTableName, TasksDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AuditingSaveChangesInterceptor(_timeProvider));

        return new TasksDbContext(optionsBuilder.Options);
    }
}
