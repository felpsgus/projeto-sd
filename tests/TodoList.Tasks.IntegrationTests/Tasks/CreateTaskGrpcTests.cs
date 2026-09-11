extern alias IdentityApi;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using TodoList.Contracts.Tasks.V1;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// <c>CreateTask</c> gRPC (BE-35) — caminho de sucesso e validação de request
/// (BE-17), migrado de <c>POST /api/tasks</c> (BE-29, removido) para o
/// cliente gRPC de <see cref="TasksGrpcTestClient"/>, mesmo padrão de
/// <c>IdentityGrpcTestClient</c>. A intenção de cada teste original é
/// preservada: status HTTP esperado → <see cref="StatusCode"/> gRPC (D-35).
/// </summary>
public sealed class CreateTaskGrpcTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebApplicationFactory<IdentityProgram> _identityFactory = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 0, 30, 0, TimeSpan.Zero));
        _identityFactory = new WebApplicationFactory<IdentityProgram>();

        _factory = new TasksApiFactory(_connection, _timeProvider, _identityFactory, new Uri("http://identity.test"));

        await _factory.EnsureDatabaseCreatedAsync();
        _client = CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _identityFactory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-03
    public async Task CreateTask_ApenasComTitle_DevolveOkComStatusPendingECompletedAtAusente()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Comprar leite" });

        reply.Status.Should().Be(ProtoTaskStatus.Pending);
        reply.CompletedAt.Should().BeNull();
    }

    [Fact] // CA-02 (id e demais campos do TaskReply — sem GET, ver nota abaixo)
    public async Task CreateTask_DevolveOTaskReplyComIdDoRecursoCriado()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Tarefa com id" });

        reply.Id.Should().NotBeNullOrEmpty();
        Guid.TryParse(reply.Id, out _).Should().BeTrue();

        // Nota: BE-17 CA-02 pede também "consultar o recurso criado". Não
        // existe RPC de leitura nesta base de código (BE-18, fora de escopo).
        // A prova de que o recurso existe é feita direto no banco.
        await using var context = _factory.CreateDbContext();
        var persisted = await context.Tasks.SingleOrDefaultAsync(task => task.Id == Guid.Parse(reply.Id));
        persisted.Should().NotBeNull();
    }

    [Fact] // CA-04
    public async Task CreateTask_SemPriority_NasceComMedium()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Sem prioridade" });

        reply.Priority.Should().Be(TaskPriority.Medium);
    }

    [Fact] // CA-05 — priority válida é preservada
    public async Task CreateTask_ComPriorityHigh_PreservaOValor()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Prioridade alta", Priority = TaskPriority.High });

        reply.Priority.Should().Be(TaskPriority.High);
    }

    [Fact] // CA-06 — verificado no banco, RN-AUTZ-01
    public async Task CreateTask_TarefaCriada_TemOwnerIdIgualAoUsuarioCorrente()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Tarefa com dono" });

        await using var context = _factory.CreateDbContext();
        var persisted = await context.Tasks.SingleAsync(task => task.Id == Guid.Parse(reply.Id));

        persisted.OwnerId.Should().Be(InMemoryUserLookup.ActiveUserId);
    }

    [Fact] // CA-07
    public async Task CreateTask_CreatedAtEUpdatedAt_VemPreenchidosEIguaisNaCriacao()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Auditoria" });

        reply.CreatedAt.ToDateTime().Should().Be(_timeProvider.GetUtcNow().UtcDateTime);
        reply.UpdatedAt.ToDateTime().Should().Be(reply.CreatedAt.ToDateTime());
    }

    [Fact] // CA-08 — título com só espaços
    public async Task CreateTask_TituloComApenasEspacos_DevolveInvalidArgumentApontandoOCampoTitle()
    {
        var exception = await CallAndCaptureFailureAsync(new CreateTaskRequest { Title = "   " });

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Trailers.GetValue("error-code").Should().Be("validation.failed");
        exception.Trailers.GetValue("validation-errors").Should().Contain("Title");
    }

    [Fact] // CA-08 — 201 caracteres
    public async Task CreateTask_TituloCom201Caracteres_DevolveInvalidArgument()
    {
        var exception = await CallAndCaptureFailureAsync(new CreateTaskRequest { Title = new string('a', 201) });

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-09 — bordas 1 e 200
    public async Task CreateTask_TituloCom1E200Caracteres_EAceito()
    {
        var curta = await CallAsync(new CreateTaskRequest { Title = "a" });
        var longa = await CallAsync(new CreateTaskRequest { Title = new string('a', 200) });

        curta.Id.Should().NotBeNullOrEmpty();
        longa.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-10 — descrição 2000 aceita, 2001 rejeitada
    public async Task CreateTask_DescricaoCom2000Aceita_2001Rejeitada()
    {
        var aceita = await CallAsync(new CreateTaskRequest { Title = "Descrição no limite", Description = new string('d', 2000) });
        var rejeitada = await CallAndCaptureFailureAsync(
            new CreateTaskRequest { Title = "Descrição acima do limite", Description = new string('d', 2001) });

        aceita.Id.Should().NotBeNullOrEmpty();
        rejeitada.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // CA-11 — dueDate no passado é aceita e marca isOverdue
    public async Task CreateTask_DueDateNoPassado_DevolveIsOverdueTrue()
    {
        var reply = await CallAsync(new CreateTaskRequest { Title = "Vencida", DueDate = "2020-01-01" });

        reply.IsOverdue.Should().BeTrue();
    }

    [Fact] // CA-12 — dueDate igual a hoje (data do relógio, sem x-client-date)
    public async Task CreateTask_DueDateIgualAHojeUtc_IsOverdueFalse()
    {
        // O relógio deste teste está em 2026-08-21T00:30 UTC; sem
        // x-client-date o fallback é a data UTC do TimeProvider (2026-08-21).
        var reply = await CallAsync(new CreateTaskRequest { Title = "Vence hoje", DueDate = "2026-08-21" });

        reply.IsOverdue.Should().BeFalse();
    }

    [Fact] // CA-12b — o cenário exato que motivou D-18
    public async Task CreateTask_ComXClientDateAnteriorAoUtc_IsOverdueUsaADataLocalDoUsuario()
    {
        // Relógio UTC em 2026-08-21T00:30 (ver InitializeAsync); usuário em
        // UTC-3 ainda está em 2026-08-20. Sem a metadata, isso venceria e
        // isOverdue seria true — com ela, D-18 exige false.
        var headers = TasksGrpcTestClient.OwnerHeaders(InMemoryUserLookup.ActiveUserId.ToString(), clientDate: "2026-08-20");

        var call = _client.CreateTaskAsync(new CreateTaskRequest { Title = "D-18", DueDate = "2026-08-20" }, headers);
        var reply = await call;

        reply.IsOverdue.Should().BeFalse();
    }

    [Theory] // CA-13
    [InlineData("31/12/2026")]
    [InlineData("2026-13-01")]
    public async Task CreateTask_DueDateEmFormatoInvalido_DevolveInvalidArgumentNaoInternal(string dueDate)
    {
        var exception = await CallAndCaptureFailureAsync(new CreateTaskRequest { Title = "Data inválida", DueDate = dueDate });

        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Trailers.GetValue("validation-errors").Should().Contain("DueDate");
    }

    // Nota: BE-17 CA-22 ("ownerId no corpo é ignorado") não se aplica mais —
    // o proto CreateTaskRequest não tem (e nunca teve) campo de dono; ele só
    // existia no corpo JSON do gatilho REST removido por esta task. O dono
    // agora só pode vir da metadata x-user-id, provado pelo teste CA-06 acima.

    private async Task<TaskReply> CallAsync(CreateTaskRequest request)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(InMemoryUserLookup.ActiveUserId.ToString());
        return await _client.CreateTaskAsync(request, headers);
    }

    private async Task<RpcException> CallAndCaptureFailureAsync(CreateTaskRequest request)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(InMemoryUserLookup.ActiveUserId.ToString());

        try
        {
            await _client.CreateTaskAsync(request, headers);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }

    private TasksGrpcTestClient CreateClient() =>
        new(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
}
