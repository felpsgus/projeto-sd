extern alias IdentityApi;

using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-35, CA-08/CA-09 — <c>CreateTask</c> sem <c>x-user-id</c> válido na
/// metadata devolve <c>Unauthenticated</c> antes de qualquer chamada ao
/// Identity (verificado apontando o Identity para um endereço morto: se o
/// interceptor não tivesse barrado a chamada antes do handler, o teste
/// travaria no timeout, não devolveria <c>Unauthenticated</c> de imediato).
/// </summary>
public sealed class RequireCallerIdentityGrpcTests : IAsyncLifetime, IDisposable
{
    private SqliteConnection _connection = null!;
    private TasksApiFactory _factory = null!;
    private TasksGrpcTestClient _client = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        // Identity roteado para um endereço morto de propósito: se
        // RequireCallerIdentityInterceptor não estivesse barrando a chamada
        // antes do handler (bug de fiação), o teste teria que esperar pelo
        // deadline gRPC do Identity em vez de falhar de imediato.
        var enderecoMorto = TasksApiFactory.GetUnreachableAddress();
        _factory = new TasksApiFactory(
            _connection, new FakeTimeProvider(DateTimeOffset.UtcNow), identityFactory: null, enderecoMorto);

        await _factory.EnsureDatabaseCreatedAsync();
        _client = new TasksGrpcTestClient(_factory.Server.CreateHandler(), _factory.Server.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // CA1001: a disposição de verdade acontece em DisposeAsync.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-08 — metadata ausente
    public async Task CreateTask_SemMetadataDeIdentidade_DevolveUnauthenticated()
    {
        var call = _client.CreateTaskAsync(new ProtoCreateTaskRequest { Title = "Sem identidade" });

        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Theory] // CA-09 — presente mas não é um Guid
    [InlineData("")]
    [InlineData("nao-e-um-guid")]
    public async Task CreateTask_ComMetadataNaoGuid_DevolveUnauthenticated(string valorDoHeader)
    {
        var headers = TasksGrpcTestClient.OwnerHeaders(valorDoHeader);

        var call = _client.CreateTaskAsync(new ProtoCreateTaskRequest { Title = "Identidade inválida" }, headers);

        var act = async () => await call;

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
