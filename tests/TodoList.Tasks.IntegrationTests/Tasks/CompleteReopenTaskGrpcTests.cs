using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using Xunit;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;
using TodoTask = TodoList.Tasks.Domain.Tasks.TodoTask;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>CompleteTask</c>/<c>ReopenTask</c> gRPC (BE-20) — CA-01 a CA-16, exceto
/// o teste transversal de autorização (coberto no Gateway). Mesma observação
/// de <see cref="GetTaskGrpcTests"/>: não fala com o Identity.
/// </summary>
public sealed class CompleteReopenTaskGrpcTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        _factory = new TasksApiFactory(_connection, _timeProvider, identityFactory: null, new Uri("http://identity.test"));

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-02 — complete em Pending: OK, completedAt preenchido
    public async Task CompleteTask_TarefaPending_RetornaOkComCompletedAtPreenchido()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);

        var reply = await CompleteAsync(owner, id);

        reply.Status.Should().Be(ProtoTaskStatus.Completed);
        reply.CompletedAt.Should().NotBeNull();
    }

    [Fact] // CA-04 — complete em tarefa já concluída: FailedPrecondition/task.already_completed
    public async Task CompleteTask_TarefaJaConcluida_RetornaFailedPreconditionComTaskAlreadyCompleted()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);
        await CompleteAsync(owner, id);

        var exception = await CompleteAndCaptureFailureAsync(owner, id);

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Trailers.GetValue("error-code").Should().Be("task.already_completed");
    }

    [Fact] // CA-06 — concluir tarefa vencida faz isOverdue virar false
    public async Task CompleteTask_TarefaVencida_TornaIsOverdueFalse()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa vencida", dueDate: new DateOnly(2020, 1, 1));

        var reply = await CompleteAsync(owner, id);

        reply.IsOverdue.Should().BeFalse();
    }

    [Fact] // CA-07, CA-08 — reopen em Completed: OK, completedAt volta a null
    public async Task ReopenTask_TarefaCompleted_RetornaOkComCompletedAtNulo()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);
        await CompleteAsync(owner, id);

        var reply = await ReopenAsync(owner, id);

        reply.Status.Should().Be(ProtoTaskStatus.Pending);
        reply.CompletedAt.Should().BeNull();
    }

    [Fact] // CA-10 — reopen em Pending: FailedPrecondition/task.not_completed
    public async Task ReopenTask_TarefaPending_RetornaFailedPreconditionComTaskNotCompleted()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);

        var exception = await ReopenAndCaptureFailureAsync(owner, id);

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Trailers.GetValue("error-code").Should().Be("task.not_completed");
    }

    [Fact] // CA-11 — reabrir tarefa vencida volta isOverdue a true
    public async Task ReopenTask_TarefaVencida_VoltaIsOverdueParaTrue()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa vencida", dueDate: new DateOnly(2020, 1, 1));
        await CompleteAsync(owner, id);

        var reply = await ReopenAsync(owner, id);

        reply.IsOverdue.Should().BeTrue();
    }

    [Fact] // CA-13 — complete/reopen em tarefa de outro dono: NotFound
    public async Task CompleteTask_TarefaDeOutroDono_RetornaNotFound()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(dono, "Não é do outro usuário", dueDate: null);

        var exception = await CompleteAndCaptureFailureAsync(outroUsuario, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task ReopenTask_TarefaDeOutroDono_RetornaNotFound()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(dono, "Não é do outro usuário", dueDate: null);
        await CompleteAsync(dono, id);

        var exception = await ReopenAndCaptureFailureAsync(outroUsuario, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-14 — complete/reopen em tarefa removida: NotFound
    public async Task CompleteTask_TarefaRemovida_RetornaNotFound()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Será removida", dueDate: null);
        await SoftDeleteAsync(id);

        var exception = await CompleteAndCaptureFailureAsync(owner, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-16 — o 409/FailedPrecondition de transição inválida é distinguível do 404 de tarefa alheia
    public async Task CompleteTask_ConflitoDeTransicao_EhDistinguivelDoNotFoundDeTarefaAlheia()
    {
        var owner = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);
        await CompleteAsync(owner, id);

        var conflito = await CompleteAndCaptureFailureAsync(owner, id);
        var notFound = await CompleteAndCaptureFailureAsync(outroUsuario, id);

        conflito.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        notFound.StatusCode.Should().Be(StatusCode.NotFound);
        conflito.StatusCode.Should().NotBe(notFound.StatusCode);
    }

    [Fact] // CA-12 — ciclo complete -> reopen -> complete: segundo completedAt é posterior ao primeiro
    public async Task Ciclo_CompleteReopenComplete_SegundoCompletedAtEhPosteriorAoPrimeiro()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa", dueDate: null);

        var primeiraConclusao = await CompleteAsync(owner, id);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        await ReopenAsync(owner, id);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));
        var segundaConclusao = await CompleteAsync(owner, id);

        segundaConclusao.CompletedAt.ToDateTime().Should().BeAfter(primeiraConclusao.CompletedAt.ToDateTime());
    }

    private async Task<Guid> SeedAsync(Guid owner, string title, DateOnly? dueDate)
    {
        await using var context = _factory.CreateDbContext();
        var task = TodoTask.Create(owner, title, null, null, dueDate, _timeProvider).Value;
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task SoftDeleteAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.SoftDelete(_timeProvider);
        await context.SaveChangesAsync();
    }

    private async Task<TaskReply> CompleteAsync(Guid caller, Guid taskId)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        return await _client.CompleteTaskAsync(new CompleteTaskRequest { Id = taskId.ToString() }, headers);
    }

    private async Task<TaskReply> ReopenAsync(Guid caller, Guid taskId)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        return await _client.ReopenTaskAsync(new ReopenTaskRequest { Id = taskId.ToString() }, headers);
    }

    private async Task<RpcException> CompleteAndCaptureFailureAsync(Guid caller, Guid taskId)
    {
        try
        {
            await CompleteAsync(caller, taskId);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }

    private async Task<RpcException> ReopenAndCaptureFailureAsync(Guid caller, Guid taskId)
    {
        try
        {
            await ReopenAsync(caller, taskId);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
