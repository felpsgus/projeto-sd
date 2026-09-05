extern alias IdentityApi;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-31, CA-07 — a evidência de que houve ida e volta pela rede entre os dois
/// serviços é o par de entradas de log com o <b>mesmo</b> <c>traceId</c>: a do
/// Tasks registrando a chamada de saída (<c>ValidateUser</c>, <c>userId</c>,
/// <c>statusCode</c>, <c>durationMs</c>) e a do Identity registrando a chamada
/// recebida com o resultado (<c>exists</c>, <c>active</c>).
///
/// <para>
/// Por que isso merece teste e não só um trecho no README: a serialização gRPC
/// é binária, então não há nada observável entre "requisição entrou no Tasks" e
/// "resposta saiu do Identity" além desses dois logs. Um roteiro que só mostra
/// o log de um dos lados não distingue uma chamada de rede de uma chamada de
/// método local — que é exatamente o que a demonstração precisa provar.
/// </para>
/// </summary>
public sealed class TraceIdCorrelationTests : IAsyncLifetime, IDisposable
{
    // Casa com "traceId=00-8f3d...-01" no fim das duas mensagens de log. O
    // traceparent do W3C não contém vírgula nem espaço, então parar no primeiro
    // separador é suficiente e não depende da ordem dos campos na mensagem.
    private static readonly Regex _traceIdNaMensagem = new(@"traceId=([^\s,]+)", RegexOptions.Compiled);

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

    // CA1001: a disposição de verdade acontece em DisposeAsync (IAsyncLifetime) —
    // mesmo padrão de CreateTaskOwnerValidationTests.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-07 — caminho de sucesso
    public async Task PostTasks_CaminhoDeSucesso_LogDosDoisServicosTemOMesmoTraceId()
    {
        var (statusCode, traceIdDoTasks, traceIdDoIdentity) =
            await PostAsync(InMemoryUserLookup.ActiveUserId);

        statusCode.Should().Be(HttpStatusCode.Created);
        AssertCorrelacionados(traceIdDoTasks, traceIdDoIdentity);
    }

    [Fact] // CA-07 — caminho de rejeição (dono inexistente)
    public async Task PostTasks_DonoInexistente_LogDosDoisServicosTemOMesmoTraceId()
    {
        var (statusCode, traceIdDoTasks, traceIdDoIdentity) = await PostAsync(Guid.NewGuid());

        statusCode.Should().Be(HttpStatusCode.NotFound);

        // A rejeição é o caso que mais importa correlacionar: é ele que prova
        // que o "não" veio do Identity, e não de uma checagem local do Tasks.
        AssertCorrelacionados(traceIdDoTasks, traceIdDoIdentity);
    }

    private static void AssertCorrelacionados(string? traceIdDoTasks, string? traceIdDoIdentity)
    {
        traceIdDoTasks.Should().NotBeNullOrWhiteSpace("o log de saída do Tasks precisa carregar o traceId para ser correlacionável");
        traceIdDoIdentity.Should().NotBeNullOrWhiteSpace("o Identity registra o traceparent recebido pela metadata gRPC");
        traceIdDoIdentity.Should().Be(traceIdDoTasks, "é o mesmo traceId nos dois lados que evidencia a ida e a volta pela rede");
    }

    /// <summary>
    /// Dispara <c>POST /api/tasks</c> com os dois serviços reais e devolve o
    /// status HTTP mais o <c>traceId</c> extraído da entrada de log de cada
    /// lado.
    /// </summary>
    private async Task<(HttpStatusCode StatusCode, string? TraceIdDoTasks, string? TraceIdDoIdentity)> PostAsync(Guid userId)
    {
        var logDoIdentity = new CapturingLoggerProvider();
        var logDoTasks = new CapturingLoggerProvider();

        await using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logDoIdentity)));

        var settings = new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" };

        await using var factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            identityFactory,
            identityFactory.Server.BaseAddress,
            settings,
            services => services.AddLogging(logging => logging.AddProvider(logDoTasks)));

        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Correlação de traceId entre os dois serviços" }),
        };
        request.Headers.Add("X-User-Id", userId.ToString());

        var response = await client.SendAsync(request);

        return (
            response.StatusCode,
            ExtrairTraceId(logDoTasks, "ValidateUser (Identity gRPC)"),
            ExtrairTraceId(logDoIdentity, "ValidateUser: userId="));
    }

    private static string? ExtrairTraceId(CapturingLoggerProvider provider, string trechoDaMensagem)
    {
        var mensagem = provider.Messages.FirstOrDefault(m => m.Contains(trechoDaMensagem, StringComparison.Ordinal));

        if (mensagem is null)
        {
            return null;
        }

        var match = _traceIdNaMensagem.Match(mensagem);

        return match.Success ? match.Groups[1].Value : null;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<string> _messages;

            public CapturingLogger(ConcurrentQueue<string> messages) => _messages = messages;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                _messages.Enqueue(formatter(state, exception));
        }
    }
}
