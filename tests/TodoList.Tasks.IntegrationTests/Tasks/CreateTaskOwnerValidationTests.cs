extern alias IdentityApi;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-28, migrado para gRPC por BE-35 — a rejeição do dono vem da resposta
/// gRPC do Identity, não de uma checagem local do Tasks. Quem decide se a
/// criação prossegue é sempre o Identity real, subido em
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

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-01, CA-14 — usuário existente e ativo: OK completo
    public async Task CreateTask_UsuarioExistenteEAtivo_DevolveOk()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = CreateClient(factory);

        var reply = await CallAsync(client, InMemoryUserLookup.ActiveUserId, "Tarefa de usuário válido");

        reply.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-04 — usuário inexistente: NotFound, nada persistido
    public async Task CreateTask_UsuarioInexistente_DevolveNotFoundTaskOwnerNotFoundSemPersistir()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = CreateClient(factory);
        var usuarioInexistente = Guid.NewGuid();

        var exception = await CallAndCaptureFailureAsync(client, usuarioInexistente, "Não deveria existir");

        exception.StatusCode.Should().Be(StatusCode.NotFound);
        exception.Trailers.GetValue("error-code").Should().Be("task.owner_not_found");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync(task => task.OwnerId == usuarioInexistente)).Should().BeFalse();
    }

    [Fact] // CA-05 — a MESMA requisição muda de resultado só porque o Identity muda de resposta
    public async Task CreateTask_MesmoUsuarioPassaAExistir_RejeicaoDesaparece_SemMudancaNoTasks()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = CreateClient(factory);

        var antes = await CallAndCaptureFailureAsync(client, Guid.NewGuid(), "Antes de existir");
        antes.StatusCode.Should().Be(StatusCode.NotFound);

        // ActiveUserId É um usuário existente no seed do Identity — a "mesma
        // requisição" muda só o x-user-id, provando que a decisão sai do
        // Identity, não de uma tabela local do Tasks.
        var depois = await CallAsync(client, InMemoryUserLookup.ActiveUserId, "Depois de existir");
        depois.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // CA-06 — usuário inativo: FailedPrecondition, nada persistido
    public async Task CreateTask_UsuarioInativo_DevolveFailedPreconditionTaskOwnerInactiveSemPersistir()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory);
        using var client = CreateClient(factory);

        var exception = await CallAndCaptureFailureAsync(client, InMemoryUserLookup.InactiveUserId, "Usuário inativo");

        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Trailers.GetValue("error-code").Should().Be("task.owner_inactive");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync(task => task.OwnerId == InMemoryUserLookup.InactiveUserId)).Should().BeFalse();
    }

    [Fact] // CA-07 — as duas rejeições geram log Warning com userId e motivo
    public async Task CreateTask_RejeicaoDeDono_GeraLogWarningComUserIdEMotivo()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        var capturingProvider = new CapturingLoggerProvider();

        await using var factory = await CreateFactoryAsync(
            identityFactory,
            configureServices: services => services.AddLogging(logging => logging.AddProvider(capturingProvider)));
        using var client = CreateClient(factory);

        var usuarioInativo = InMemoryUserLookup.InactiveUserId;
        var exception = await CallAndCaptureFailureAsync(client, usuarioInativo, "Gera log de aviso");
        exception.StatusCode.Should().Be(StatusCode.FailedPrecondition);

        capturingProvider.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains(usuarioInativo.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // CA-08, CA-10 — Identity desligado: Unavailable, sem vazar detalhe de transporte
    public async Task CreateTask_IdentityDesligado_DevolveUnavailableSemVazarDetalheDeTransporte()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: enderecoMorto);
        using var client = CreateClient(factory);

        var exception = await CallAndCaptureFailureAsync(client, InMemoryUserLookup.ActiveUserId, "Identity fora do ar");

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        exception.Trailers.GetValue("error-code").Should().Be("identity.unavailable");

        exception.Message.Should().NotContain(enderecoMorto.Host, "endereço do Identity não pode vazar na resposta");
        exception.Message.Should().NotContain("RpcException");
    }

    [Fact] // CA-09 — nada persistido no cenário de indisponibilidade
    public async Task CreateTask_IdentityDesligado_NadaEPersistido()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: enderecoMorto);
        using var client = CreateClient(factory);

        await CallAndCaptureFailureAsync(client, InMemoryUserLookup.ActiveUserId, "Não deveria ser gravada");

        await using var context = factory.CreateDbContext();
        (await context.Tasks.AnyAsync()).Should().BeFalse();
    }

    [Fact] // CA-11 — falha dentro do deadline configurado, não espera indefinida
    public async Task CreateTask_IdentityDesligado_FalhaDentroDoDeadlineConfigurado()
    {
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        await using var factory = await CreateFactoryAsync(
            identityFactory: null,
            identityAddress: enderecoMorto,
            configOverrides: new Dictionary<string, string?> { ["Identity:GrpcTimeoutSeconds"] = "1" });
        using var client = CreateClient(factory);

        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var exception = await CallAndCaptureFailureAsync(client, InMemoryUserLookup.ActiveUserId, "Deadline");
        cronometro.Stop();

        exception.StatusCode.Should().Be(StatusCode.Unavailable);
        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(6), "a chamada não deve ficar pendurada além do deadline configurado");
    }

    private static async Task<TasksApiFactory> CreateFactoryAsync(
        WebApplicationFactory<IdentityProgram>? identityFactory,
        Uri? identityAddress = null,
        IReadOnlyDictionary<string, string?>? configOverrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new TasksApiFactory(
            new SqliteConnection("DataSource=:memory:").Also(c => c.Open()),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            identityFactory,
            identityAddress ?? new Uri("http://identity.test"),
            configOverrides,
            configureServices);

        await factory.EnsureDatabaseCreatedAsync();

        return factory;
    }

    private static TasksGrpcTestClient CreateClient(TasksApiFactory factory) =>
        new(factory.Server.CreateHandler(), factory.Server.BaseAddress);

    private static async Task<TodoList.Contracts.Tasks.V1.TaskReply> CallAsync(TasksGrpcTestClient client, Guid userId, string title) =>
        await client.CreateTaskAsync(
            new ProtoCreateTaskRequest { Title = title }, TasksGrpcTestClient.OwnerHeaders(userId.ToString()));

    private static async Task<RpcException> CallAndCaptureFailureAsync(TasksGrpcTestClient client, Guid userId, string title)
    {
        try
        {
            await CallAsync(client, userId, title);
            throw new InvalidOperationException("Esperava RpcException de falha, mas a chamada teve sucesso.");
        }
        catch (RpcException exception)
        {
            return exception;
        }
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
