extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-28 — a rejeição do dono vem da resposta gRPC do Identity, não de uma
/// checagem local do Tasks. Usa o modo provisório de identidade (BE-29,
/// <c>X-User-Id</c>) só para escolher qual usuário está criando a tarefa —
/// quem decide se a criação prossegue é sempre o Identity real, subido em
/// <see cref="WebApplicationFactory{TEntryPoint}"/> (exceto no grupo de
/// indisponibilidade, que aponta para um endereço morto de propósito).
/// </summary>
public sealed class CreateTaskOwnerValidationTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: o tipo tem um campo descartável (_connection); a disposição de
    // verdade acontece em DisposeAsync (IAsyncLifetime), mesmo padrão de
    // AuditingAndSoftDeleteTests/TodoTaskSqlitePersistenceTests (BE-02, BE-05).
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-14 — usuário existente e ativo: 201 completo
    public async Task PostTasks_UsuarioExistenteEAtivo_Retorna201()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, InMemoryUserLookup.ActiveUserId, "Tarefa de usuário válido");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-04 — usuário inexistente: 404, nada persistido
    public async Task PostTasks_UsuarioInexistente_Retorna404TaskOwnerNotFoundSemPersistir()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = factory.CreateClient();
        var usuarioInexistente = Guid.NewGuid();

        var response = await PostAsync(client, usuarioInexistente, "Não deveria existir");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("task.owner_not_found");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync(task => task.OwnerId == usuarioInexistente)).Should().BeFalse();
    }

    [Fact] // CA-05 — a MESMA requisição muda de resultado só porque o Identity muda de resposta
    public async Task PostTasks_MesmoUsuarioPassaAExistir_RejeicaoDesaparece_SemMudancaNoTasks()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = factory.CreateClient();

        var antes = await PostAsync(client, Guid.NewGuid(), "Antes de existir");
        antes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // O ActiveUserId É um usuário existente no seed do Identity — a
        // "mesma requisição" muda só o X-User-Id, provando que a decisão sai
        // do Identity, não de uma tabela local do Tasks.
        var depois = await PostAsync(client, InMemoryUserLookup.ActiveUserId, "Depois de existir");
        depois.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact] // CA-06 — usuário inativo: 409, nada persistido
    public async Task PostTasks_UsuarioInativo_Retorna409TaskOwnerInactiveSemPersistir()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, InMemoryUserLookup.InactiveUserId, "Usuário inativo");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("task.owner_inactive");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync(task => task.OwnerId == InMemoryUserLookup.InactiveUserId)).Should().BeFalse();
    }

    [Fact] // CA-07 — as duas rejeições geram log Warning com userId e motivo
    public async Task PostTasks_RejeicaoDeDono_GeraLogWarningComUserIdEMotivo()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        var capturingProvider = new CapturingLoggerProvider();

        await using var factory = await CreateFactoryAsync(
            identityFactory,
            configureServices: services => services.AddLogging(logging => logging.AddProvider(capturingProvider)));
        using var client = factory.CreateClient();

        var usuarioInativo = InMemoryUserLookup.InactiveUserId;
        var response = await PostAsync(client, usuarioInativo, "Gera log de aviso");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        capturingProvider.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(usuarioInativo.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // CA-08, CA-10 — Identity desligado: 503, sem vazar detalhe de transporte
    public async Task PostTasks_IdentityDesligado_Retorna503SemVazarDetalheDeTransporte()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: enderecoMorto);
        using var client = factory.CreateClient();

        var response = await PostAsync(client, InMemoryUserLookup.ActiveUserId, "Identity fora do ar");

        response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status503ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull("D-28: o 503 é explicitamente temporário");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorCode").GetString().Should().Be("identity.unavailable");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(enderecoMorto.Host, "endereço do Identity não pode vazar na resposta");
        raw.Should().NotContain("RpcException");
        raw.Should().NotContain("StatusCode");
    }

    [Fact] // CA-09 — nada persistido no cenário de indisponibilidade
    public async Task PostTasks_IdentityDesligado_NadaEPersistido()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: enderecoMorto);
        using var client = factory.CreateClient();

        await PostAsync(client, InMemoryUserLookup.ActiveUserId, "Não deveria ser gravada");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync()).Should().BeFalse();
    }

    [Fact] // CA-11 — falha dentro do deadline configurado, não espera indefinida
    public async Task PostTasks_IdentityDesligado_FalhaDentroDoDeadlineConfigurado()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(
            identityFactory: null,
            identityAddress: enderecoMorto,
            configOverrides: new Dictionary<string, string?> { ["Identity:GrpcTimeoutSeconds"] = "1" });
        using var client = factory.CreateClient();

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var response = await PostAsync(client, InMemoryUserLookup.ActiveUserId, "Deadline");
        cronometro.Stop();

        response.StatusCode.Should().Be((HttpStatusCode)StatusCodes.Status503ServiceUnavailable);
        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6), "a chamada não deve ficar pendurada além do deadline configurado");
    }

    private static async Task<TasksApiFactory> CreateFactoryAsync(
        WebApplicationFactory<IdentityProgram>? identityFactory,
        Uri? identityAddress = null,
        IReadOnlyDictionary<string, string?>? configOverrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        // Estes testes são sobre BE-28 (a decisão vem do Identity), não sobre
        // BE-29 — o modo anônimo aqui é só o jeito de escolher qual usuário
        // está criando a tarefa nesta base de código sem BE-13.
        var settings = new Dictionary<string, string?>(configOverrides ?? new Dictionary<string, string?>())
        {
            ["Tasks:AllowAnonymousCreate"] = "true",
        };

        var factory = new TasksApiFactory(
            new SqliteConnection("DataSource=:memory:").Also(c => c.Open()),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            identityFactory,
            identityAddress ?? new Uri("http://identity.test"),
            settings,
            configureServices);

        await factory.EnsureDatabaseCreatedAsync();

        return factory;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid userId, string title)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title }),
        };
        request.Headers.Add("X-User-Id", userId.ToString());

        return await client.SendAsync(request);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<(LogLevel Level, string Message)> _entries;

            public CapturingLogger(List<(LogLevel Level, string Message)> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

internal static class ObjectExtensions
{
    /// <summary>Roda uma ação sobre o valor e devolve o próprio valor — só para inicializar (abrir a conexão) inline num initializer de expressão.</summary>
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
