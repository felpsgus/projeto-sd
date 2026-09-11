extern alias IdentityApi;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Application.Users;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Domain.Tasks;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-17 — limite de tarefas ativas (RN-TASK-15, D-08), migrado para gRPC por
/// BE-35. "Ativa" = <b>não concluída E não removida</b> (nota técnica de
/// BE-17). Usa <c>Tasks:MaxActivePerUser</c> reduzido e um
/// <see cref="IUserLookup"/> de teste que trata qualquer <see cref="Guid"/>
/// como usuário ativo — necessário para exercitar CA-18 (limite por usuário)
/// com dois donos arbitrários.
/// </summary>
public sealed class CreateTaskActiveLimitTests : IAsyncLifetime, IDisposable
{
    private const int ReducedLimit = 3;

    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebApplicationFactory<IdentityProgram> _identityFactory = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddSingleton<IUserLookup>(new AnyGuidIsActiveUserLookup())));

        _factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:MaxActivePerUser"] = ReducedLimit.ToString() });

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _identityFactory.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-14, CA-19 — limite reduzido bloqueia na (limite + 1)-ésima criação
    public async Task CreateTask_NoLimiteReduzido_ACriacaoQueAtingeOLimiteSucede_APosLimiteFalha()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit - 1);

        var atingeOLimite = await CallAsync(owner, "Última vaga");
        atingeOLimite.Id.Should().NotBeNullOrEmpty();

        var exception = await CallAndCaptureFailureAsync(owner, "Sem vaga");
        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Trailers.GetValue("error-code").Should().Be("task.active_limit_reached");
    }

    [Fact] // CA-15 — mensagem clara com o valor do limite
    public async Task CreateTask_AoAtingirOLimite_MensagemInformaOLimiteDeFormaClara()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit);

        var exception = await CallAndCaptureFailureAsync(owner, "Sem vaga");

        exception.Status.Detail.Should().Contain(ReducedLimit.ToString());
    }

    [Fact] // CA-16 — tarefas concluídas não contam para o limite
    public async Task CreateTask_ComTarefasConcluidasNoLimite_AindaPermiteCriarPendente()
    {
        var owner = Guid.NewGuid();
        await SeedCompletedTasksAsync(owner, ReducedLimit);

        var reply = await CallAsync(owner, "Concluídas não contam");

        reply.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-17 — soft delete libera espaço imediatamente
    public async Task CreateTask_ApósSoftDeleteDeUmaAtiva_LiberaEspacoNoLimite()
    {
        var owner = Guid.NewGuid();
        var ids = await SeedPendingTasksAsync(owner, ReducedLimit);

        // No limite — a próxima criação falharia se nada mudasse.
        (await CallAndCaptureFailureAsync(owner, "Sem vaga antes do soft delete")).StatusCode.Should().Be(StatusCode.FailedPrecondition);

        await SoftDeleteAsync(ids[0]);

        var reply = await CallAsync(owner, "Cabe depois do soft delete");
        reply.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-18 — o limite é por usuário
    public async Task CreateTask_UsuarioNoLimite_NaoAfetaOutroUsuarioComZeroTarefas()
    {
        var primeiroUsuario = Guid.NewGuid();
        var segundoUsuario = Guid.NewGuid();
        await SeedPendingTasksAsync(primeiroUsuario, ReducedLimit);

        (await CallAndCaptureFailureAsync(primeiroUsuario, "Primeiro, sem vaga")).StatusCode.Should().Be(StatusCode.FailedPrecondition);

        var reply = await CallAsync(segundoUsuario, "Segundo, com vaga");
        reply.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-20 — limite nulo desativa o bloqueio
    public async Task CreateTask_ComLimiteNulo_NaoHaBloqueioMesmoComMuitasTarefasAtivas()
    {
        await using var connectionSemLimite = new SqliteConnection("DataSource=:memory:");
        await connectionSemLimite.OpenAsync();

        // PostConfigure roda depois do Bind feito em Program.cs (CA-20: o
        // valor null é o que desativa o limite).
        await using var factorySemLimite = new TasksApiFactory(
            connectionSemLimite,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            configureServices: services => services.PostConfigure<TaskOptions>(options => options.MaxActivePerUser = null));
        await factorySemLimite.EnsureDatabaseCreatedAsync();
        using var clientSemLimite = new TasksGrpcTestClient(factorySemLimite.Server.CreateHandler(), factorySemLimite.Server.BaseAddress);

        var owner = Guid.NewGuid();
        await using (var context = factorySemLimite.CreateDbContext())
        {
            for (var i = 0; i < ReducedLimit + 5; i++)
            {
                context.Tasks.Add(TodoTask.Create(owner, $"Tarefa {i}", null, null, null, _timeProvider).Value);
            }

            await context.SaveChangesAsync();
        }

        var reply = await clientSemLimite.CreateTaskAsync(
            new ProtoCreateTaskRequest { Title = "Sem limite" }, TasksGrpcTestClient.OwnerHeaders(owner.ToString()));

        reply.Id.Should().NotBeNullOrEmpty();
    }

    private async Task<List<Guid>> SeedPendingTasksAsync(Guid owner, int count)
    {
        var ids = new List<Guid>();
        await using var context = _factory.CreateDbContext();

        for (var i = 0; i < count; i++)
        {
            var task = TodoTask.Create(owner, $"Ativa {i}", null, null, null, _timeProvider).Value;
            context.Tasks.Add(task);
            ids.Add(task.Id);
        }

        await context.SaveChangesAsync();

        return ids;
    }

    private async Task SeedCompletedTasksAsync(Guid owner, int count)
    {
        await using var context = _factory.CreateDbContext();

        for (var i = 0; i < count; i++)
        {
            var task = TodoTask.Create(owner, $"Concluída {i}", null, null, null, _timeProvider).Value;
            task.Complete(_timeProvider);
            context.Tasks.Add(task);
        }

        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteAsync(Guid taskId)
    {
        await using var context = _factory.CreateDbContext();
        var task = await context.Tasks.SingleAsync(t => t.Id == taskId);
        task.SoftDelete(_timeProvider);
        await context.SaveChangesAsync();
    }

    private async Task<TodoList.Contracts.Tasks.V1.TaskReply> CallAsync(Guid owner, string title) =>
        await _client.CreateTaskAsync(new ProtoCreateTaskRequest { Title = title }, TasksGrpcTestClient.OwnerHeaders(owner.ToString()));

    private async Task<RpcException> CallAndCaptureFailureAsync(Guid owner, string title)
    {
        try
        {
            await CallAsync(owner, title);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
    }

    /// <summary>Trata qualquer <see cref="Guid"/> como usuário existente e ativo — só para os testes de limite (CA-18 precisa de donos arbitrários).</summary>
    private sealed class AnyGuidIsActiveUserLookup : IUserLookup
    {
        public Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserLookupResult?>(new UserLookupResult(Active: true, DisplayName: "Usuário de teste"));
    }
}
