using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using TodoList.Tasks.Domain.Tasks;
using Xunit;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>GetTask</c> gRPC (BE-41, recorte de BE-18) — CA-12 a CA-16. Mesma
/// observação de <see cref="ListTasksGrpcTests"/>: não fala com o Identity,
/// então <see cref="TasksApiFactory"/> recebe <c>identityFactory: null</c>.
/// </summary>
public sealed class GetTaskGrpcTests : IAsyncLifetime, IDisposable
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

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-12 — tarefa própria: TaskReply completo, com isOverdue calculado
    public async Task GetTask_TarefaPropria_RetornaTaskReplyCompletoComIsOverdue()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Minha tarefa", dueDate: new DateOnly(2020, 1, 1));

        var reply = await GetAsync(owner, id);

        reply.Id.Should().Be(id.ToString());
        reply.Title.Should().Be("Minha tarefa");
        reply.IsOverdue.Should().BeTrue();
    }

    [Fact] // CA-13 — id inexistente: NotFound
    public async Task GetTask_IdInexistente_RetornaNotFound()
    {
        var exception = await GetAndCaptureFailureAsync(Guid.NewGuid(), Guid.NewGuid());

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-14 — tarefa de outro dono: NotFound, nunca a tarefa
    public async Task GetTask_TarefaDeOutroDono_RetornaNotFound()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(dono, "Não é do outro usuário");

        var exception = await GetAndCaptureFailureAsync(outroUsuario, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-15 — inexistente e de outro dono são indistinguíveis
    public async Task GetTask_RespostasDeInexistenteEDeOutroDono_SaoIndistinguiveis()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var idAlheio = await SeedAsync(dono, "Tarefa alheia");

        var excecaoInexistente = await GetAndCaptureFailureAsync(outroUsuario, Guid.NewGuid());
        var excecaoAlheia = await GetAndCaptureFailureAsync(outroUsuario, idAlheio);

        excecaoInexistente.StatusCode.Should().Be(excecaoAlheia.StatusCode);
        excecaoInexistente.Trailers.GetValue("error-code").Should().Be(excecaoAlheia.Trailers.GetValue("error-code"));
        excecaoInexistente.Status.Detail.Should().Be(excecaoAlheia.Status.Detail);
    }

    [Fact] // CA-16 — tarefa própria removida: mesmo NotFound
    public async Task GetTask_TarefaPropriaRemovida_RetornaNotFound()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Será removida");
        await SoftDeleteAsync(id);

        var exception = await GetAndCaptureFailureAsync(owner, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // Segunda linha de defesa: id em formato inválido é InvalidArgument, não NotFound nem 500
    public async Task GetTask_IdEmFormatoInvalido_DevolveInvalidArgument()
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(Guid.NewGuid().ToString());

        var call = _client.GetTaskAsync(new GetTaskRequest { Id = "não-é-um-guid" }, headers);
        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    private async Task<Guid> SeedAsync(Guid owner, string title, DateOnly? dueDate = null)
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

    private async Task<TaskReply> GetAsync(Guid caller, Guid taskId)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        return await _client.GetTaskAsync(new GetTaskRequest { Id = taskId.ToString() }, headers);
    }

    private async Task<RpcException> GetAndCaptureFailureAsync(Guid caller, Guid taskId)
    {
        try
        {
            await GetAsync(caller, taskId);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
