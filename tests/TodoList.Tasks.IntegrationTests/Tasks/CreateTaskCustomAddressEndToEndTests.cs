extern alias IdentityApi;

using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-30, CA-06 — ponta a ponta: com o endereço do Identity vindo só de
/// configuração, <c>CreateTask</c> (gRPC, BE-35) completa a criação passando
/// pelo Identity mesmo quando ele não está no endereço padrão de
/// desenvolvimento (<c>5081</c>).
///
/// <para>
/// <b>O que este teste NÃO cobre, de propósito.</b> Os dois serviços aqui
/// rodam no mesmo processo de teste, cada um atrás de um
/// <see cref="WebApplicationFactory{TEntryPoint}"/> — isto é, sobre
/// <c>TestServer</c> (transporte em memória), não sobre <c>Kestrel</c> real
/// escutando em uma porta TCP. O roteiro manual equivalente está no README
/// ("Configuração"), agora via cliente gRPC (grpcurl) em vez de <c>curl</c>.
/// </para>
/// </summary>
public sealed class CreateTaskCustomAddressEndToEndTests : IAsyncLifetime, IDisposable
{
    private static readonly Uri[] _enderecosDeDesenvolvimento =
    [
        new("http://localhost:5080"), new("http://localhost:5081"), new("http://localhost:5100"), new("http://localhost:5101"),
    ];

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

    [Fact] // CA-06
    public async Task CreateTask_ComIdentityEmEnderecoNaoPadrao_CriaATarefaPassandoPeloIdentity()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();

        // O endereço de um WebApplicationFactory nunca é uma das portas fixas
        // de desenvolvimento (é atribuído pelo TestServer) — a asserção abaixo
        // não é só documentação, ela derruba o teste se algum dia deixar de
        // ser verdade.
        var enderecoDoIdentity = identityFactory.Server.BaseAddress;
        _enderecosDeDesenvolvimento.Should().NotContain(enderecoDoIdentity, "o cenário de CA-06 é exatamente um endereço fora dos padrões de dev");

        await using var factory = new TasksApiFactory(_connection, _timeProvider, identityFactory, enderecoDoIdentity);
        await factory.EnsureDatabaseCreatedAsync();
        using var client = new TasksGrpcTestClient(factory.Server.CreateHandler(), factory.Server.BaseAddress);

        var reply = await client.CreateTaskAsync(
            new ProtoCreateTaskRequest { Title = "Criada com Identity em endereço não padrão" },
            TasksGrpcTestClient.OwnerHeaders(InMemoryUserLookup.ActiveUserId.ToString()));

        reply.Id.Should().NotBeNullOrEmpty("o Identity respondeu num endereço não padrão e mesmo assim a criação foi completada");
    }
}
