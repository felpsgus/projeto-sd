extern alias IdentityApi;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Infrastructure.Users;
using Xunit;
using IdentityProgram = IdentityApi::Program;

namespace TodoList.Tasks.IntegrationTests.Tasks;

/// <summary>
/// BE-30, CA-06 — ponta a ponta: com o endereço do Identity vindo só de
/// configuração, <c>POST /api/tasks</c> completa a criação passando pelo
/// Identity mesmo quando ele não está no endereço padrão de desenvolvimento
/// (<c>5081</c>). Reaproveita <see cref="TasksApiFactory"/> exatamente como
/// <c>CreateTaskOwnerValidationTests</c> — a novidade aqui não é o mecanismo
/// (já coberto por CA-03/CA-04 em <c>GrpcIdentityGatewayIntegrationTests</c>,
/// com variável de ambiente real), é a asserção explícita de que o endereço
/// usado não é nenhuma das portas de desenvolvimento (CA-06 fala em "portas
/// diferentes das padrão").
///
/// <para>
/// <b>O que este teste NÃO cobre, de propósito.</b> Os dois serviços aqui
/// rodam no mesmo processo de teste, cada um atrás de um
/// <see cref="WebApplicationFactory{TEntryPoint}"/> — isto é, sobre
/// <c>TestServer</c> (transporte em memória), não sobre <c>Kestrel</c> real
/// escutando em uma porta TCP. Não há como <c>TestServer</c> provar que
/// <c>Kestrel:Endpoints:Http:Url</c>/<c>Kestrel:Endpoints:Grpc:Url</c>
/// aceitam porta não padrão por variável de ambiente — isso é contrato do
/// próprio ASP.NET Core/Kestrel, não deste serviço, e reproduzir os dois
/// processos reais aqui (com sincronização de porta efêmera entre eles) tem
/// custo desproporcional ao que resta provar. O roteiro abaixo é o caminho
/// manual equivalente, citado também no README ("Configuração"):
/// </para>
///
/// <code>
/// # terminal 1 — Identity em portas não padrão, só por variável de ambiente
/// $env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6080"
/// $env:Kestrel__Endpoints__Grpc__Url = "http://0.0.0.0:6081"
/// dotnet run --project src/Identity/TodoList.Identity.Api
///
/// # terminal 2 — Tasks em porta não padrão, apontando para o Identity acima
/// $env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6100"
/// $env:Identity__GrpcAddress = "http://localhost:6081"
/// dotnet run --project src/Tasks/TodoList.Tasks.Api
///
/// # terminal 3 — usuário ativo do seed em memória (README)
/// curl -X POST http://localhost:6100/api/tasks `
///   -H "Content-Type: application/json" `
///   -H "X-User-Id: 10000000-0000-0000-0000-000000000001" `
///   -d '{"title":"teste manual de CA-06"}'
/// # esperado: 201 Created — nenhum appsettings*.json foi editado, só variável de ambiente.
/// </code>
/// </summary>
public sealed class CreateTaskCustomAddressEndToEndTests : IAsyncLifetime, IDisposable
{
    private static readonly Uri[] _enderecosDeDesenvolvimento =
    [
        new("http://localhost:5080"), new("http://localhost:5081"), new("http://localhost:5100"),
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

    // CA1001: a disposição de verdade acontece em DisposeAsync (IAsyncLifetime) —
    // mesmo padrão de CreateTaskOwnerValidationTests.
    public void Dispose() => GC.SuppressFinalize(this);

    [Fact] // CA-06
    public async Task PostTasks_ComIdentityEmEnderecoNaoPadrao_CriaATarefaPassandoPeloIdentity()
    {
        await using var identityFactory = new WebApplicationFactory<IdentityProgram>();

        // O endereço de um WebApplicationFactory nunca é uma das portas fixas
        // de desenvolvimento (é atribuído pelo TestServer) — a asserção abaixo
        // não é só documentação, ela derruba o teste se algum dia deixar de
        // ser verdade (ex.: uma mudança no runtime de teste passar a usar um
        // desses valores por coincidência).
        var enderecoDoIdentity = identityFactory.Server.BaseAddress;
        _enderecosDeDesenvolvimento.Should().NotContain(enderecoDoIdentity, "o cenário de CA-06 é exatamente um endereço fora dos padrões de dev");

        var settings = new Dictionary<string, string?> { ["Tasks:AllowAnonymousCreate"] = "true" };
        await using var factory = new TasksApiFactory(_connection, _timeProvider, identityFactory, enderecoDoIdentity, settings);
        await factory.EnsureDatabaseCreatedAsync();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tasks")
        {
            Content = JsonContent.Create(new { title = "Criada com Identity em endereço não padrão" }),
        };
        request.Headers.Add("X-User-Id", InMemoryUserLookup.ActiveUserId.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created, "o Identity respondeu num endereço não padrão e mesmo assim a criação foi completada");
    }
}
