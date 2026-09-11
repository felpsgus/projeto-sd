// Ver o comentário de GatewayApiFactory.cs — extern alias evita a colisão de
// nomes entre o lado cliente do proto (TodoList.Gateway.Api) e o lado
// servidor (TodoList.Gateway.IntegrationTests.Fakes), ambos no mesmo
// csharp_namespace. Aqui só é preciso para nomear FakeTaskReply/FakeCreateTaskRequest
// explicitamente ao configurar o dublê do Tasks.
extern alias Fakes;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using FakeCreateTaskRequest = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeCreateTaskRequest;
using FakeTaskReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTaskReply;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-36 — <c>POST /api/tasks</c>: CA-01, CA-04 a CA-08, CA-16 a CA-18,
/// CA-25, CA-26 e o repasse de metadata (<c>x-user-id</c>, <c>x-client-date</c>).
/// </summary>
public class CreateTaskTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public CreateTaskTests(GatewayApiFactory factory)
    {
        _factory = factory;
        _factory.Identity.ValidateTokenHandler = _ => (true, AuthenticatedUserId);
    }

    [Fact] // CA-01, CA-04
    public async Task CreateTask_TokenValidoEPayloadValido_Retorna201ComLocationECorpoTraduzido()
    {
        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid().ToString();
        _factory.Tasks.CreateTaskHandler = request => new FakeTaskReply(
            taskId, request.Title, request.Description, "Medium", "Pending", request.DueDate, false, null, now, now);

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Comprar leite", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be($"/api/tasks/{taskId}");

        var rawBody = await response.Content.ReadAsStringAsync();
        // CA-04: nenhum nome de tipo/campo do proto (ex.: "TaskReply", ou o
        // campo em snake_case "is_overdue") aparece no corpo — só o DTO
        // HTTP, em camelCase.
        rawBody.Should().NotContain("TaskReply");
        rawBody.Should().NotContain("is_overdue");

        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(taskId);
        body.Title.Should().Be("Comprar leite");
        body.Priority.Should().Be("Medium");
        body.Status.Should().Be("Pending");
    }

    [Fact] // CA-05
    public async Task CreateTask_TituloAusente_Retorna400ESemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.CreateTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest(null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.CreateTaskCallCount.Should().Be(callCountAntes, "nenhuma chamada gRPC CreateTask deve ocorrer com payload inválido");
    }

    [Fact] // CA-06
    public async Task CreateTask_PrioridadeForaDoEnum_Retorna400NaoQuinhentos()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, "Urgente", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-07
    public async Task CreateTask_JsonMalformado_Retorna400ProblemDetailsNaoQuinhentos()
    {
        var client = AuthenticatedClient();

        using var content = new StringContent("""{"title": "Sem fechar aspas, vírgula sobrando,}""", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/api/tasks", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact] // CA-08
    public async Task CreateTask_DueDateForaDoFormato_Retorna400()
    {
        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, null, "31/12/2026"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact] // CA-16
    public async Task CreateTask_TasksDevolveNotFound_Retorna404ComErrorCodePreservado()
    {
        _factory.Tasks.CreateTaskHandler = _ =>
        {
            var trailers = new Metadata { { "error-code", "owner.not_found" } };
            throw new RpcException(new Status(StatusCode.NotFound, "Dono não encontrado."), trailers);
        };

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("owner.not_found");
    }

    [Theory] // CA-17
    [InlineData("owner_inactive")]
    [InlineData("active_limit_reached")]
    public async Task CreateTask_TasksDevolveFailedPrecondition_Retorna409ComErrorCodeDistinguindoOsCasos(string errorCode)
    {
        _factory.Tasks.CreateTaskHandler = _ =>
        {
            var trailers = new Metadata { { "error-code", errorCode } };
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Conflito de estado."), trailers);
        };

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(errorCode);
    }

    [Theory] // CA-18
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task CreateTask_TasksIndisponivelOuDeadline_Retorna503ComRetryAfter(StatusCode statusCode)
    {
        _factory.Tasks.CreateTaskHandler = _ => throw new RpcException(new Status(statusCode, "Tasks fora do ar."));

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact] // CA-25 — traceparent chega às duas chamadas gRPC de saída
    public async Task CreateTask_TraceparentDaRequisicaoDeEntrada_ChegaAoValidateTokenEAoCreateTask()
    {
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.CreateTaskHandler = request =>
            new FakeTaskReply(Guid.NewGuid().ToString(), request.Title, null, "Medium", "Pending", null, false, null, now, now);

        using var activity = new Activity("teste-traceparent").Start();

        var client = AuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Título", null, null, null));

        activity.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        _factory.Identity.LastValidateTokenRequestHeaders!.Get("traceparent").Should().NotBeNull();
        _factory.Tasks.LastCreateTaskRequestHeaders!.Get("traceparent").Should().NotBeNull();
    }

    [Fact] // D-34 — x-user-id (claim sub) e x-client-date (repassado do header de entrada)
    public async Task CreateTask_MetadataDeSaida_TemUserIdDoClaimSubEClientDateRepassado()
    {
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.CreateTaskHandler = request =>
            new FakeTaskReply(Guid.NewGuid().ToString(), request.Title, null, "Medium", "Pending", null, false, null, now, now);

        var client = AuthenticatedClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/tasks", UriKind.Relative))
        {
            Content = JsonContent.Create(new CreateTaskHttpRequest("Título", null, null, null)),
        };
        request.Headers.Add("X-Client-Date", "2026-09-10");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        _factory.Tasks.LastCreateTaskRequestHeaders!.Get("x-user-id")!.Value.Should().Be(AuthenticatedUserId);
        _factory.Tasks.LastCreateTaskRequestHeaders!.Get("x-client-date")!.Value.Should().Be("2026-09-10");
    }

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-de-teste");

        return client;
    }
}
