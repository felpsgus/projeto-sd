// Ver o comentário de GatewayApiFactory.cs — extern alias evita a colisão de
// nomes entre o lado cliente do proto (TodoList.Gateway.Api) e o lado
// servidor (TodoList.Gateway.IntegrationTests.Fakes), ambos no mesmo
// csharp_namespace.
extern alias Fakes;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using FakeListTasksReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeListTasksReply;
using FakeTaskReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTaskReply;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-41 — <c>GET /api/tasks</c>: CA-17 a CA-19, CA-24.</summary>
public class ListTasksTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public ListTasksTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-17
    public async Task ListTasks_TokenValido_Retorna200ComCorpoPaginado()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new FakeTaskReply(
            Guid.NewGuid().ToString(), "Tarefa 1", null, "Medium", "Pending", null, false, null, now, now);
        _factory.Tasks.ListTasksHandler = request => new FakeListTasksReply([item], request.Page, request.PageSize, 1);

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?page=1&pageSize=20", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<ListTasksHttpResponse>();
        body.Should().NotBeNull();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
        body.TotalCount.Should().Be(1);
        body.Items.Should().ContainSingle();
        body.Items[0].Title.Should().Be("Tarefa 1");
    }

    [Fact] // CA-24 — nenhum tipo gerado pelo proto (nem snake_case) vaza no corpo
    public async Task ListTasks_CorpoDeResposta_NaoExpoeTipoOuCampoDoProto()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new FakeTaskReply(
            Guid.NewGuid().ToString(), "Tarefa 1", null, "Medium", "Pending", null, false, null, now, now);
        _factory.Tasks.ListTasksHandler = request => new FakeListTasksReply([item], request.Page, request.PageSize, 1);

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks", UriKind.Relative));

        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContain("ListTasksReply");
        rawBody.Should().NotContain("total_count");
        rawBody.Should().NotContain("is_overdue");
    }

    [Fact] // CA-18
    public async Task ListTasks_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/tasks", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-19
    public async Task ListTasks_PageZero_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.ListTasksCallCount;

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?page=0", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.ListTasksCallCount.Should().Be(callCountAntes, "nenhuma chamada gRPC ListTasks deve ocorrer com page fora da faixa");
    }

    [Theory] // CA-19
    [InlineData(0)]
    [InlineData(101)]
    public async Task ListTasks_PageSizeForaDaFaixa_Retorna400SemChamarOTasks(int pageSize)
    {
        var callCountAntes = _factory.Tasks.ListTasksCallCount;

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks?pageSize={pageSize}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.ListTasksCallCount.Should().Be(callCountAntes, "nenhuma chamada gRPC ListTasks deve ocorrer com pageSize fora da faixa");
    }

    [Fact] // CA-19 — regressão de camelCase: a chave de errors não pode ser "Page"/"PageSize"
    public async Task ListTasks_PageInvalida_ChaveDeErrosEmCamelCase()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?page=0", UriKind.Relative));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"page\"");
        body.Should().NotContain("\"Page\"");
    }

    [Fact]
    public async Task ListTasks_SemParametros_AplicaOsPadroesEChamaOTasks()
    {
        var callCountAntes = _factory.Tasks.ListTasksCallCount;
        _factory.Tasks.ListTasksHandler = request => new FakeListTasksReply([], request.Page, request.PageSize, 0);

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Tasks.ListTasksCallCount.Should().Be(callCountAntes + 1);

        var body = await response.Content.ReadFromJsonAsync<ListTasksHttpResponse>();
        body!.Page.Should().Be(1);
        body.PageSize.Should().Be(20);
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
