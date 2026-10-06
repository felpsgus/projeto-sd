extern alias IdentityApi;

using System.Net;
using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;
using TodoList.Identity.Infrastructure.Users;
using TodoList.Tasks.IntegrationTests.Tasks;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// BE-24 — <c>/health/ready</c> do Tasks reporta o alcance do Identity como <b>Degraded</b> (200, nunca derruba o
/// serviço nem o probe gRPC do Cloud Run, D-37) e o log de requisição carrega <c>service</c> e, só quando há
/// metadata <c>x-user-id</c>, <c>userId</c> (CA-03, CA-07, CA-07b).
/// </summary>
public sealed class ObservabilityTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-07
    public async Task GetHealthReady_IdentityInalcancavel_RespondeDegradadoComStatus200()
    {
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: TasksApiFactory.GetUnreachableAddress());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Degraded não tira o Tasks do ar");
        (await response.Content.ReadAsStringAsync()).Should().Be("Degraded");
    }

    [Fact] // CA-07
    public async Task GetHealthReady_IdentityNoAr_RespondeHealthy()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(identityFactory, new Uri("http://identity.test"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact] // CA-07, D-37 — o probe gRPC do Cloud Run só olha o banco: Identity fora não o derruba
    public async Task GrpcHealthCheck_IdentityInalcancavel_ContinuaServing()
    {
        await using var factory = await CreateFactoryAsync(identityFactory: null, identityAddress: TasksApiFactory.GetUnreachableAddress());
        using var channel = GrpcChannel.ForAddress(
            factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = factory.Server.CreateHandler() });

        var response = await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest());

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact] // CA-03, CA-07b
    public async Task LogDeRequisicao_TemServiceETemUserIdSoQuandoHaMetadataXUserId()
    {
        var sink = new CapturingLogSink();
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();
        await using var factory = await CreateFactoryAsync(
            identityFactory, new Uri("http://identity.test"), services => services.AddSingleton<ILogEventSink>(sink));

        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var grpc = new TasksGrpcTestClient(factory.Server.CreateHandler(), factory.Server.BaseAddress);
        await grpc.CreateTaskAsync(
            new ProtoCreateTaskRequest { Title = "Log de requisição" },
            TasksGrpcTestClient.OwnerHeaders(InMemoryUserLookup.SeedUserId.ToString()));

        var requestLogs = await WaitForRequestLogsAsync(sink, expected: 2);

        var live = requestLogs.Single(entry => Property(entry, "RequestPath") == "/health/live");
        var create = requestLogs.Single(entry => Property(entry, "RequestPath") == "/tasks.v1.TasksService/CreateTask");

        Property(live, "service").Should().Be("tasks");
        live.Properties.ContainsKey("userId").Should().BeFalse("anônimo não leva campo userId");
        Property(create, "service").Should().Be("tasks");
        Property(create, "userId").Should().Be(InMemoryUserLookup.SeedUserId.ToString());
    }

    // O log de requisição sai depois da resposta: espera (até 5 s) em vez de ler o sink às cegas.
    private static async Task<List<Serilog.Events.LogEvent>> WaitForRequestLogsAsync(CapturingLogSink sink, int expected)
    {
        List<Serilog.Events.LogEvent> logs = [];

        for (var attempt = 0; attempt < 100 && logs.Count < expected; attempt++)
        {
            logs = sink.Events
                .Where(entry => Property(entry, "SourceContext") == "Serilog.AspNetCore.RequestLoggingMiddleware")
                .ToList();

            if (logs.Count < expected)
            {
                await Task.Delay(50);
            }
        }

        return logs;
    }

    /// <summary>Valor string de uma propriedade do evento (sem as aspas da renderização do Serilog).</summary>
    private static string? Property(Serilog.Events.LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) ? value.ToString().Trim('"') : null;

    private async Task<TasksApiFactory> CreateFactoryAsync(
        WebApplicationFactory<IdentityProgram>? identityFactory,
        Uri identityAddress,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new TasksApiFactory(
            _connection,
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)),
            identityFactory,
            identityAddress,
            new Dictionary<string, string?> { ["Identity:GrpcTimeoutSeconds"] = "1" },
            configureServices);

        await factory.EnsureDatabaseCreatedAsync();

        return factory;
    }
}
