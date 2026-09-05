extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
/// BE-29 — gatilho HTTP provisório: header <c>X-User-Id</c>
/// (<c>Tasks:AllowAnonymousCreate=true</c>) e o que muda quando o modo está
/// desligado (padrão, <c>false</c>). CA-07 (sem token → 401) e CA-08 (header
/// ignorado com token válido) **não** são fechados aqui — não há mecanismo de
/// token nesta base de código (BE-13 fora de escopo); ver o relatório final.
/// </summary>
public sealed class CreateTaskAnonymousModeTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private FakeTimeProvider _timeProvider = null!;
    private WebApplicationFactory<IdentityProgram> _identityFactory = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        _identityFactory = new WebApplicationFactory<IdentityProgram>();
    }

    public async Task DisposeAsync() => await _identityFactory.DisposeAsync();

    // CA1001: mesmo padrão de CreateTaskOwnerValidationTests — a disposição
    // de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Theory] // CA-03 — ausente, vazio, não-Guid
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    public async Task PostTasks_ModoAnonimoComXUserIdInvalido_Retorna400ApontandoOHeader(string? valorDoHeader)
    {
        await using var factory = CreateAnonymousFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Sem dono válido" }),
        };

        if (valorDoHeader is not null)
        {
            request.Headers.Add("X-User-Id", valorDoHeader);
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("X-User-Id");
    }

    [Fact] // CA-06 — aviso de inicialização quando o modo está ligado
    public async Task Startup_ComModoAnonimoLigado_EmiteLogDeAviso()
    {
        var capturingProvider = new CapturingLoggerProvider();

        await using var factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" },
            configureServices: services => services.AddLogging(logging => logging.AddProvider(capturingProvider)));

        // Força a construção do host (o log de startup roda em Program.cs).
        using var client = factory.CreateClient();

        capturingProvider.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("AllowAnonymousCreate", StringComparison.Ordinal));
    }

    [Fact] // Documenta (sem fechar) o comportamento do modo desligado: ver CA-07/CA-08 no relatório.
    public async Task PostTasks_ModoDesligado_ComOuSemXUserId_FalhaDaMesmaForma_PorNaoHaverAutenticacaoAinda()
    {
        // Este teste NÃO fecha BE-29 CA-07 (sem token → 401) nem CA-08 (header
        // ignorado com token válido): não existe conceito de token nesta base
        // de código (BE-13 fora do escopo desta rodada). O que ele prova é o
        // subconjunto observável hoje: no modo definitivo, o header X-User-Id
        // não influencia o desfecho — presente ou ausente, o resultado é
        // idêntico (ICurrentUser não registra HeaderCurrentUser neste modo).
        await using var factory = new TasksApiFactory(
            _connection,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "false" });
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateClient();

        using var semHeader = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Sem header" }),
        };
        using var comHeader = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Com header" }),
        };
        comHeader.Headers.Add("X-User-Id", InMemoryUserLookup.ActiveUserId.ToString());

        var respostaSemHeader = await client.SendAsync(semHeader);
        var respostaComHeader = await client.SendAsync(comHeader);

        respostaSemHeader.StatusCode.Should().Be(respostaComHeader.StatusCode, "no modo desligado, X-User-Id não deveria influenciar o desfecho");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync()).Should().BeFalse("nenhuma tarefa deveria ser criada sem identidade resolvida");
    }

    private TasksApiFactory CreateAnonymousFactory() =>
        new(
            _connection,
            _timeProvider,
            _identityFactory,
            new Uri("http://identity.test"),
            new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" });

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
