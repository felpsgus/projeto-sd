extern alias IdentityApi;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-31/CA-16 de BE-35 — a evidência de que houve ida e volta pela rede
/// entre os dois serviços é o par de entradas de log com o <b>mesmo</b>
/// <c>traceId</c>: a do Tasks registrando a chamada recebida
/// (<c>CreateTask</c>, <c>ownerId</c>, <c>statusCode</c>, <c>durationMs</c>) e
/// a do Identity registrando a chamada de <c>ValidateUser</c> recebida com o
/// resultado (<c>exists</c>, <c>active</c>).
/// </summary>
public sealed class TraceIdCorrelationTests : IAsyncLifetime, IDisposable
{
    // Casa com "traceId=00-8f3d...-01" no fim das duas mensagens de log.
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

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-16 — caminho de sucesso
    public async Task CreateTask_CaminhoDeSucesso_LogDosDoisServicosTemOMesmoTraceId()
    {
        var (statusCode, traceIdDoTasks, traceIdDoIdentity) = await CallAsync(InMemoryUserLookup.ActiveUserId);

        statusCode.Should().Be(StatusCode.OK);
        AssertCorrelacionados(traceIdDoTasks, traceIdDoIdentity);
    }

    [Fact] // CA-16 — caminho de rejeição (dono inexistente)
    public async Task CreateTask_DonoInexistente_LogDosDoisServicosTemOMesmoTraceId()
    {
        var (statusCode, traceIdDoTasks, traceIdDoIdentity) = await CallAsync(Guid.NewGuid());

        statusCode.Should().Be(StatusCode.NotFound);

        // A rejeição é o caso que mais importa correlacionar: é ele que prova
        // que o "não" veio do Identity, e não de uma checagem local do Tasks.
        AssertCorrelacionados(traceIdDoTasks, traceIdDoIdentity);
    }

    private static void AssertCorrelacionados(string? traceIdDoTasks, string? traceIdDoIdentity)
    {
        traceIdDoTasks.Should().NotBeNullOrWhiteSpace("o log de CreateTask do Tasks precisa carregar o traceId para ser correlacionável");
        traceIdDoIdentity.Should().NotBeNullOrWhiteSpace("o Identity registra o traceparent recebido pela metadata gRPC");
        traceIdDoIdentity.Should().Be(traceIdDoTasks, "é o mesmo traceId nos dois lados que evidencia a ida e a volta pela rede");
    }

    /// <summary>
    /// Dispara <c>CreateTask</c> com os dois serviços reais e devolve o
    /// <see cref="StatusCode"/> mais o <c>traceId</c> extraído da entrada de
    /// log de cada lado.
    /// </summary>
    private async Task<(StatusCode StatusCode, string? TraceIdDoTasks, string? TraceIdDoIdentity)> CallAsync(Guid userId)
    {
        var logDoIdentity = new CapturingLoggerProvider();
        var logDoTasks = new CapturingLoggerProvider();

        await using var identityFactory = new WebApplicationFactory<IdentityProgram>()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddLogging(logging => logging.AddProvider(logDoIdentity))));

        await using var factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            identityFactory,
            identityFactory.Server.BaseAddress,
            configureServices: services => services.AddLogging(logging => logging.AddProvider(logDoTasks)));

        await factory.EnsureDatabaseCreatedAsync();
        using var client = new TasksGrpcTestClient(factory.Server.CreateHandler(), factory.Server.BaseAddress);

        var request = new ProtoCreateTaskRequest { Title = "Correlação de traceId entre os dois serviços" };
        var headers = TasksGrpcTestClient.OwnerHeaders(userId.ToString());

        StatusCode statusCode;

        try
        {
            await client.CreateTaskAsync(request, headers);
            statusCode = StatusCode.OK;
        }
        catch (RpcException exception)
        {
            statusCode = exception.StatusCode;
        }

        return (
            statusCode,
            ExtrairTraceId(logDoTasks, "CreateTask: ownerId="),
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
