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
using FakeUpdateTaskRequest = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeUpdateTaskRequest;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-19 — <c>PUT /api/tasks/{id}</c>: sucesso, 401 sem token, 400 de validação e de id malformado, 404 de tarefa alheia/inexistente.</summary>
public class UpdateTaskTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public UpdateTaskTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateTask_TarefaPropriaComCorpoValido_Retorna200ComTaskAtualizada()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.UpdateTaskHandler = request =>
            new FakeTaskReply(request.Id, request.Title, request.Description, request.Priority, "Pending", request.DueDate, false, null, now, now);

        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/tasks/{taskId}", UriKind.Relative),
            new UpdateTaskHttpRequest("Novo título", "Nova descrição", "High", "2026-12-31"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Id.Should().Be(taskId);
        body.Title.Should().Be("Novo título");
        body.Description.Should().Be("Nova descrição");
        body.Priority.Should().Be("High");
        body.DueDate.Should().Be("2026-12-31");
    }

    [Fact] // BE-19, nota técnica: campos omitidos no corpo chegam ausentes ao Tasks (que aplica o padrão de substituição)
    public async Task UpdateTask_DescricaoEDueDateNulos_ChegamAusentesAoTasksSemDefinirOCampoOptional()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        FakeUpdateTaskRequest? captured = null;
        _factory.Tasks.UpdateTaskHandler = request =>
        {
            captured = request;

            return new FakeTaskReply(request.Id, request.Title, request.Description, "Medium", "Pending", request.DueDate, false, null, now, now);
        };

        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/tasks/{taskId}", UriKind.Relative),
            new UpdateTaskHttpRequest("Só título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Description.Should().BeNull();
        captured.DueDate.Should().BeNull();
        captured.Priority.Should().Be("Unspecified", "prioridade ausente vira TASK_PRIORITY_UNSPECIFIED — quem aplica o padrão Média é o Tasks");

        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Description.Should().BeNull();
        body.DueDate.Should().BeNull();
    }

    [Fact]
    public async Task UpdateTask_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative),
            new UpdateTaskHttpRequest("Título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateTask_TituloAusente_Retorna400ESemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.UpdateTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative),
            new UpdateTaskHttpRequest(null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.UpdateTaskCallCount.Should().Be(callCountAntes, "nenhuma chamada gRPC UpdateTask deve ocorrer com payload inválido");
    }

    [Fact]
    public async Task UpdateTask_PrioridadeForaDoEnum_Retorna400()
    {
        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative),
            new UpdateTaskHttpRequest("Título", null, "Urgente", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateTask_IdNaoEhGuid_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.UpdateTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/tasks/abc", UriKind.Relative),
            new UpdateTaskHttpRequest("Título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.UpdateTaskCallCount.Should().Be(callCountAntes, "id malformado não deve gerar chamada gRPC UpdateTask");
    }

    [Fact] // 404 de tarefa alheia/inexistente com corpos idênticos (RN-AUTZ-03)
    public async Task UpdateTask_TarefaAlheiaOuInexistente_DevolvemCorposIdenticos()
    {
        _factory.Tasks.UpdateTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();
        var request = new UpdateTaskHttpRequest("Título", null, null, null);

        var responseInexistente = await client.PutAsJsonAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative), request);
        var responseAlheia = await client.PutAsJsonAsync(new Uri($"/api/tasks/{Guid.NewGuid()}", UriKind.Relative), request);

        responseInexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        responseAlheia.StatusCode.Should().Be(HttpStatusCode.NotFound);

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

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
