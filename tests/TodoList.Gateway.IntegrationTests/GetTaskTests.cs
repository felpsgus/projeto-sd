// Ver o comentário de GatewayApiFactory.cs — extern alias evita a colisão de
// nomes entre o lado cliente do proto (TodoList.Gateway.Api) e o lado
// servidor (TodoList.Gateway.IntegrationTests.Fakes), ambos no mesmo
// csharp_namespace.
extern alias Fakes;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using FakeTaskReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTaskReply;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-41 — <c>GET /api/tasks/{id}</c>: CA-20 a CA-23.</summary>
public class GetTaskTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public GetTaskTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-20
    public async Task GetTask_TarefaPropria_Retorna200ComTaskHttpResponse()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.GetTaskHandler = id => new FakeTaskReply(id, "Tarefa", null, "Medium", "Pending", null, false, null, now, now);

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks/{taskId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Id.Should().Be(taskId);
        body.Title.Should().Be("Tarefa");
    }

    [Fact] // CA-21
    public async Task GetTask_TarefaInexistente_Retorna404()
    {
        _factory.Tasks.GetTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // CA-21 — corpos idênticos entre tarefa alheia e inexistente (RN-AUTZ-03)
    public async Task GetTask_TarefaAlheiaOuInexistente_DevolvemCorposIdenticos()
    {
        _factory.Tasks.GetTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var responseInexistente = await client.GetAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));
        var responseAlheia = await client.GetAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative));

        responseInexistente.StatusCode.Should().Be(responseAlheia.StatusCode);

        // "traceId" é único por requisição (ApiErrorHandlingExtensions) — a
        // comparação byte a byte precisa ignorar só esse campo; todo o resto
        // do ProblemDetails (title/status/detail/type) deve ser idêntico.
        var bodyInexistente = await WithoutTraceIdAsync(responseInexistente);
        var bodyAlheia = await WithoutTraceIdAsync(responseAlheia);

        bodyInexistente.Should().Be(bodyAlheia);
    }

    private static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var properties = document.RootElement.EnumerateObject().Where(property => property.Name != "traceId");

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in properties)
            {
                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Fact] // CA-22
    public async Task GetTask_IdNaoEhGuid_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.GetTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks/abc", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.GetTaskCallCount.Should().Be(callCountAntes, "id malformado não deve gerar chamada gRPC GetTask");
    }

    [Fact] // CA-22 — regressão de camelCase da correção adjacente
    public async Task GetTask_IdNaoEhGuid_ChaveDeErrosEmCamelCase()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks/abc", UriKind.Relative));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"id\"");
    }

    [Fact] // CA-23 — o Location do 201 de POST resolve num GET subsequente, com o mesmo token
    public async Task GetTask_ViaLocationDoPostAnterior_Resolve200()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.CreateTaskHandler = request =>
            new FakeTaskReply(taskId, request.Title, request.Description, "Medium", "Pending", request.DueDate, false, null, now, now);
        _factory.Tasks.GetTaskHandler = id => new FakeTaskReply(id, "Comprar leite", null, "Medium", "Pending", null, false, null, now, now);

        var client = AuthenticatedClient();

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Comprar leite", null, null, null));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = createResponse.Headers.Location!;

        var getResponse = await client.GetAsync(location);

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Id.Should().Be(taskId);
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
