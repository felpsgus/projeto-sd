using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-21 — <c>DELETE /api/tasks/{id}</c>: 204 sem corpo, 401, 400 de id malformado, 404 de tarefa alheia/inexistente.</summary>
public class DeleteTaskTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public DeleteTaskTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DeleteTask_TarefaPropria_Retorna204SemCorpo()
    {
        var taskId = Guid.NewGuid().ToString();
        string? deletedId = null;
        _factory.Tasks.DeleteTaskHandler = id => deletedId = id;

        var client = AuthenticatedClient();

        var response = await client.DeleteAsync(new Uri($"/api/tasks/{taskId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().BeEmpty();
        deletedId.Should().Be(taskId);
    }

    [Fact]
    public async Task DeleteTask_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteTask_IdNaoEhGuid_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.DeleteTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.DeleteAsync(new Uri("/api/tasks/abc", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.DeleteTaskCallCount.Should().Be(callCountAntes, "id malformado não deve gerar chamada gRPC DeleteTask");
    }

    [Fact]
    public async Task DeleteTask_TarefaInexistente_Retorna404()
    {
        _factory.Tasks.DeleteTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var response = await client.DeleteAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // RN-AUTZ-03 — corpo idêntico ao de id inexistente (mesmo NotFound, sem distinção de causa)
    public async Task DeleteTask_TarefaAlheiaOuInexistente_DevolvemMesmoStatusCode()
    {
        _factory.Tasks.DeleteTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var responseInexistente = await client.DeleteAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));
        var responseAlheia = await client.DeleteAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));

        responseInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        responseAlheia.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
