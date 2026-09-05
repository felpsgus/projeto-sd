extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-17 — limite de tarefas ativas (RN-TASK-15, D-08). "Ativa" =
/// <b>não concluída E não removida</b> (nota técnica de BE-17: o defeito mais
/// provável é essa definição). Usa <c>Tasks:MaxActivePerUser</c> reduzido
/// (nunca 500 tarefas reais em teste, conforme "Testes obrigatórios" de
/// BE-17) e um <see cref="IUserLookup"/> de teste que trata qualquer
/// <see cref="Guid"/> como usuário ativo — necessário para exercitar CA-18
/// (limite por usuário) com dois donos arbitrários, já que o seed fixo do
/// Identity (<c>InMemoryUserLookup</c>) só conhece dois ids.
/// </summary>
public sealed class CreateTaskActiveLimitTests : IAsyncLifetime, IDisposable
{
    private const int ReducedLimit = 3;

    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebApplicationFactory<IdentityProgram> _identityFactory = null!;
    private TasksApiFactory _factory = null!;
    private HttpClient _client = null!;

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
            new Dictionary<string, string?>
            {
                ["Tasks:MaxActivePerUser"] = ReducedLimit.ToString(),
                ["Tasks:AllowAnonymousCreate"] = "true",
            });

        await _factory.EnsureDatabaseCreatedAsync();
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _identityFactory.DisposeAsync();
    }

    // CA1001: mesmo padrão de CreateTaskOwnerValidationTests — a disposição
    // de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-14, CA-19 — limite reduzido bloqueia na (limite + 1)-ésima criação
    public async Task PostTasks_NoLimiteReduzido_ACriacaoQueAtingeOLimiteSucede_APosLimiteFalha()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit - 1);

        var atingeOLimite = await PostAsync(owner, "Última vaga");
        atingeOLimite.StatusCode.Should().Be(HttpStatusCode.Created);

        var apósOLimite = await PostAsync(owner, "Sem vaga");
        apósOLimite.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await apósOLimite.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("task.active_limit_reached");
    }

    [Fact] // CA-15 — mensagem clara com o valor do limite
    public async Task PostTasks_AoAtingirOLimite_MensagemInformaOLimiteDeFormaClara()
    {
        var owner = Guid.NewGuid();
        await SeedPendingTasksAsync(owner, ReducedLimit);

        var response = await PostAsync(owner, "Sem vaga");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var detail = body.GetProperty("detail").GetString();

        detail.Should().Contain(ReducedLimit.ToString());
    }

    [Fact] // CA-16 — tarefas concluídas não contam para o limite
    public async Task PostTasks_ComTarefasConcluidasNoLimite_AindaPermiteCriarPendente()
    {
        var owner = Guid.NewGuid();
        await SeedCompletedTasksAsync(owner, ReducedLimit);

        var response = await PostAsync(owner, "Concluídas não contam");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-17 — soft delete libera espaço imediatamente
    public async Task PostTasks_ApósSoftDeleteDeUmaAtiva_LiberaEspacoNoLimite()
    {
        var owner = Guid.NewGuid();
        var ids = await SeedPendingTasksAsync(owner, ReducedLimit);

        // No limite — a próxima criação falharia se nada mudasse.
        (await PostAsync(owner, "Sem vaga antes do soft delete")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await SoftDeleteAsync(ids[0]);

        var response = await PostAsync(owner, "Cabe depois do soft delete");
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-18 — o limite é por usuário
    public async Task PostTasks_UsuarioNoLimite_NaoAfetaOutroUsuarioComZeroTarefas()
    {
        var primeiroUsuario = Guid.NewGuid();
        var segundoUsuario = Guid.NewGuid();
        await SeedPendingTasksAsync(primeiroUsuario, ReducedLimit);

        (await PostAsync(primeiroUsuario, "Primeiro, sem vaga")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var response = await PostAsync(segundoUsuario, "Segundo, com vaga");
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-20 — limite nulo desativa o bloqueio
    public async Task PostTasks_ComLimiteNulo_NaoHaBloqueioMesmoComMuitasTarefasAtivas()
    {
        await using var connectionSemLimite = new SqliteConnection("DataSource=:memory:");
        await connectionSemLimite.OpenAsync();

        // PostConfigure roda depois do Bind feito em Program.cs (CA-20: o
        // valor null é o que desativa o limite) — mais direto e livre de
        // ambiguidade de representação textual do que tentar expressar
        // "null" numa chave de configuração in-memory.
        await using var factorySemLimite = new TasksApiFactory(
            connectionSemLimite,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" },
            configureServices: services => services.PostConfigure<TaskOptions>(options => options.MaxActivePerUser = null));
        await factorySemLimite.EnsureDatabaseCreatedAsync();
        using var clientSemLimite = factorySemLimite.CreateClient();

        var owner = Guid.NewGuid();
        await using (var context = factorySemLimite.CreateDbContext())
        {
            for (var i = 0; i < ReducedLimit + 5; i++)
            {
                context.Tasks.Add(TodoTask.Create(owner, $"Tarefa {i}", null, null, null, _timeProvider).Value);
            }

            await context.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Sem limite" }),
        };
        request.Headers.Add("X-User-Id", owner.ToString());

        var response = await clientSemLimite.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
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

    private async Task<HttpResponseMessage> PostAsync(Guid owner, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title }),
        };
        request.Headers.Add("X-User-Id", owner.ToString());

        return await _client.SendAsync(request);
    }

    /// <summary>Trata qualquer <see cref="Guid"/> como usuário existente e ativo — só para os testes de limite (CA-18 precisa de donos arbitrários).</summary>
    private sealed class AnyGuidIsActiveUserLookup : IUserLookup
    {
        public Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<UserLookupResult?>(new UserLookupResult(Active: true, DisplayName: "Usuário de teste"));
    }
}
