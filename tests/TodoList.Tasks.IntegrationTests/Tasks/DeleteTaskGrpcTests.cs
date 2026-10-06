using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using Xunit;
using TodoTask = TodoList.Tasks.Domain.Tasks.TodoTask;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>DeleteTask</c> gRPC (BE-21) — CA-01 a CA-13 (exceto o teste transversal
/// de autorização, coberto no Gateway; e CA-06, coberto por
/// <see cref="Persistence.DeleteTaskPostgresSoftDeleteTests"/>, que precisa de
/// um banco real para exercitar <c>CountActiveByOwnerAsync</c> em conjunto com
/// <c>CreateTaskHandler</c>). Não fala com o Identity, mesma observação de
/// <see cref="GetTaskGrpcTests"/>.
/// </summary>
public sealed class DeleteTaskGrpcTests : IAsyncLifetime, IDisposable
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

    [Fact] // CA-01 — tarefa própria: OK (Empty)
    public async Task DeleteTask_TarefaPropria_RetornaOk()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");

        var reply = await DeleteAsync(owner, id);

        reply.Should().Be(new Empty());
    }

    [Fact] // CA-02 — a linha continua no banco com DeletedAt preenchido (IgnoreQueryFilters)
    public async Task DeleteTask_TarefaPropria_MantemALinhaNoBancoComDeletedAtPreenchido()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");

        await DeleteAsync(owner, id);

        await using var context = _factory.CreateDbContext();
        var linha = await context.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        linha.DeletedAt.Should().NotBeNull();
    }

    [Fact] // CA-03 — updatedAt é atualizado pela remoção
    public async Task DeleteTask_TarefaPropria_AtualizaUpdatedAt()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");

        _timeProvider.Advance(TimeSpan.FromMinutes(7));
        await DeleteAsync(owner, id);

        await using var context = _factory.CreateDbContext();
        var linha = await context.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        linha.UpdatedAt.Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
    }

    [Fact] // CA-04 — GET depois de removida: NotFound
    public async Task DeleteTask_DepoisDaRemocao_GetTaskRetornaNotFound()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");
        await DeleteAsync(owner, id);

        var headers = TasksGrpcTestClient.OwnerHeaders(owner.ToString());
        var act = async () => await _client.GetTaskAsync(new GetTaskRequest { Id = id.ToString() }, headers);

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-05 — depois de removida, some da listagem
    public async Task DeleteTask_DepoisDaRemocao_SomeDaListagem()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");
        await DeleteAsync(owner, id);

        var headers = TasksGrpcTestClient.OwnerHeaders(owner.ToString());
        var reply = await _client.ListTasksAsync(new ListTasksRequest(), headers);

        reply.Items.Should().NotContain(item => item.Id == id.ToString());
        reply.TotalCount.Should().Be(0);
    }

    [Fact] // CA-07 — tarefa concluída também pode ser removida
    public async Task DeleteTask_TarefaConcluida_TambemPodeSerRemovida()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");
        await CompleteAsync(owner, id);

        var reply = await DeleteAsync(owner, id);

        reply.Should().Be(new Empty());
    }

    [Fact] // CA-08 — remover tarefa já removida: NotFound (idempotência escolhida, D-07/BE-21)
    public async Task DeleteTask_TarefaJaRemovida_RetornaNotFound()
    {
        var owner = Guid.NewGuid();
        var id = await SeedAsync(owner, "Tarefa");
        await DeleteAsync(owner, id);

        var exception = await DeleteAndCaptureFailureAsync(owner, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-09 — tarefa de outro dono: NotFound, e a tarefa da vítima permanece intacta
    public async Task DeleteTask_TarefaDeOutroDono_RetornaNotFoundEMantemATarefaIntacta()
    {
        var dono = Guid.NewGuid();
        var outroUsuario = Guid.NewGuid();
        var id = await SeedAsync(dono, "Não é do outro usuário");

        var exception = await DeleteAndCaptureFailureAsync(outroUsuario, id);

        exception.StatusCode.Should().Be(StatusCode.NotFound);

        await using var context = _factory.CreateDbContext();
        var linha = await context.Tasks.SingleAsync(t => t.Id == id);
        linha.DeletedAt.Should().BeNull();
    }

    [Fact] // CA-10 — id inexistente: NotFound
    public async Task DeleteTask_IdInexistente_RetornaNotFound()
    {
        var exception = await DeleteAndCaptureFailureAsync(Guid.NewGuid(), Guid.NewGuid());

        exception.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact] // CA-13 — a remoção de uma tarefa não afeta outra tarefa do mesmo usuário
    public async Task DeleteTask_NaoAfetaOutraTarefaDoMesmoUsuario()
    {
        var owner = Guid.NewGuid();
        var idParaRemover = await SeedAsync(owner, "Será removida");
        var idIntacta = await SeedAsync(owner, "Deve continuar intacta");

        await DeleteAsync(owner, idParaRemover);

        await using var context = _factory.CreateDbContext();
        var linhaIntacta = await context.Tasks.SingleAsync(t => t.Id == idIntacta);
        linhaIntacta.DeletedAt.Should().BeNull();
    }

    [Fact] // Segunda linha de defesa: id em formato inválido é InvalidArgument
    public async Task DeleteTask_IdEmFormatoInvalido_DevolveInvalidArgument()
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(Guid.NewGuid().ToString());

        var call = _client.DeleteTaskAsync(new DeleteTaskRequest { Id = "não-é-um-guid" }, headers);
        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    private async Task<Guid> SeedAsync(Guid owner, string title)
    {
        await using var context = _factory.CreateDbContext();
        var task = TodoTask.Create(owner, title, null, null, null, _timeProvider).Value;
        context.Tasks.Add(task);
        await context.SaveChangesAsync();

        return task.Id;
    }

    private async Task CompleteAsync(Guid caller, Guid taskId)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        await _client.CompleteTaskAsync(new CompleteTaskRequest { Id = taskId.ToString() }, headers);
    }

    private async Task<Empty> DeleteAsync(Guid caller, Guid taskId)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(caller.ToString());
        return await _client.DeleteTaskAsync(new DeleteTaskRequest { Id = taskId.ToString() }, headers);
    }

    private async Task<RpcException> DeleteAndCaptureFailureAsync(Guid caller, Guid taskId)
    {
        try
        {
            await DeleteAsync(caller, taskId);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }
}
