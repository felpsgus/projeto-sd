using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;
using ProtoReopenTaskRequest = TodoList.Contracts.Tasks.V1.ReopenTaskRequest;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// Decisão do cliente de 23/09/2026: o limite de tarefas ativas (RN-TASK-15)
/// também vale para a reabertura (BE-20), não só para a criação (BE-17) —
/// mesmo mecanismo (<see cref="TaskOptions.MaxActivePerUser"/>,
/// <c>CountActiveByOwnerAsync</c> e <c>task.active_limit_reached</c>), agora
/// aplicado por <see cref="ReopenTaskHandler"/>. Mesmo padrão de
/// <see cref="CreateTaskActiveLimitTests"/>, sem depender do Identity — o
/// reopen não o consulta, igual a <see cref="CompleteReopenTaskGrpcTests"/>.
/// </summary>
public sealed class ReopenTaskActiveLimitTests : IAsyncLifetime, IDisposable
{
    private const int ReducedLimit = 3;

    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            identityFactory: null,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:MaxActivePerUser"] = ReducedLimit.ToString() });

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // Reabrir no limite falha com o mesmo erro da criação
    public async Task ReopenTask_ComContagemJaNoLimite_FalhaComActiveLimitReached()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit);
        var completedId = await SeedCompletedTaskAsync(owner);

        var exception = await ReopenAndCaptureFailureAsync(owner, completedId);

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Trailers.GetValue("error-code").Should().Be("task.active_limit_reached");
    }

    [Fact] // Reabrir abaixo do limite funciona normalmente
    public async Task ReopenTask_ComContagemAbaixoDoLimite_Sucede()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit - 1);
        var completedId = await SeedCompletedTaskAsync(owner);

        var reply = await ReopenAsync(owner, completedId);

        reply.Status.Should().Be(TodoList.Contracts.Tasks.V1.TaskStatus.Pending);
    }

    [Fact] // A própria tarefa concluída não conta como ativa antes de reabrir: com o limite já ocupado só pelas
           // outras pendentes, ela ainda cabe como a última vaga.
    public async Task ReopenTask_TarefaConcluidaNaoContaComoAtivaAntesDeReabrir_CabeNaUltimaVaga()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit - 1);
        var completedId = await SeedCompletedTaskAsync(owner);

        var reply = await ReopenAsync(owner, completedId);

        reply.Status.Should().Be(TodoList.Contracts.Tasks.V1.TaskStatus.Pending);

        // Com a vaga agora ocupada, uma reabertura adicional falharia.
        var outraCompletedId = await SeedCompletedTaskAsync(owner);
        var exception = await ReopenAndCaptureFailureAsync(owner, outraCompletedId);
        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    private async Task SeedPendingTasksAsync(Guid owner, int count)
    {
        await using var context = _factory.CreateDbContext();

        for (var i = 0; i < count; i++)
        {
            context.Tasks.Add(TodoTask.Create(owner, $"Ativa {i}", null, null, null, _timeProvider).Value);
        }

        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedCompletedTaskAsync(Guid owner)
    {
        await using var context = _factory.CreateDbContext();
        var task = TodoTask.Create(owner, "Concluída", null, null, null, _timeProvider).Value;
        task.Complete(_timeProvider);
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task<ProtoTaskReply> ReopenAsync(Guid owner, Guid taskId) =>
        await _client.ReopenTaskAsync(
            new ProtoReopenTaskRequest { Id = taskId.ToString() }, TasksGrpcTestClient.OwnerHeaders(owner.ToString()));

    private async Task<RpcException> ReopenAndCaptureFailureAsync(Guid owner, Guid taskId)
    {
        try
        {
            await ReopenAsync(owner, taskId);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
