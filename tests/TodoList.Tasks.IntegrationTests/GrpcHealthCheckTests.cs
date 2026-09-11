using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TodoList.Tasks.IntegrationTests.Tasks;
using Xunit;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// BE-35, CA-14, D-37 — o gRPC Health Checking Protocol
/// (<c>grpc.health.v1.Health/Check</c>) responde <c>SERVING</c> com o Tasks
/// no ar e o banco acessível. O banco real é Postgres (D-17); aqui o
/// <see cref="TasksApiFactory"/> substitui por SQLite in-memory já criado
/// (mesma técnica do resto desta pasta) — <c>AddDbContextCheck</c> chama
/// <c>Database.CanConnectAsync()</c>, que a conexão SQLite aberta satisfaz
/// igualmente, sem precisar de Docker. A cobertura equivalente com Postgres
/// real fica no teste Testcontainers <c>HealthReadyComBancoRealTests</c>.
///
/// <para>
/// <b>Armadilha evitada (BE-35):</b> este teste também prova, por não enviar
/// nenhuma metadata <c>x-user-id</c>, que <c>RequireCallerIdentityInterceptor</c>
/// está registrado <b>só</b> para <c>TasksGrpcService</c> — se estivesse
/// registrado globalmente, esta chamada teria sido rejeitada com
/// <c>Unauthenticated</c> antes de chegar ao serviço de health.
/// </para>
/// </summary>
public sealed class GrpcHealthCheckTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private TasksApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new TasksApiFactory(
            _connection, new FakeTimeProvider(DateTimeOffset.UtcNow), identityFactory: null, new Uri("http://identity.test"));

        await _factory.EnsureDatabaseCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-14
    public async Task Check_SemMetadataDeIdentidade_RespondeServing()
    {
        using var channel = GrpcChannel.ForAddress(
            _factory.Server.BaseAddress, new GrpcChannelOptions { HttpHandler = _factory.Server.CreateHandler() });
        var client = new Health.HealthClient(channel);

        var response = await client.CheckAsync(new HealthCheckRequest());

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }
}
