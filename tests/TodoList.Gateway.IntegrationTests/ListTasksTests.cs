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
using FakeListTasksRequest = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeListTasksRequest;
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

    [Theory] // BE-22, CA-05
    [InlineData("pending", "Pending")]
    [InlineData("completed", "Completed")]
    [InlineData("all", "All")]
    public async Task ListTasks_StatusIsolado_ChegaAoTasksResolvido(string status, string expectedProtoStatus)
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks?status={status}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(expectedProtoStatus);
    }

    [Fact] // BE-22, CA-06
    public async Task ListTasks_PrioridadeUnica_ChegaAoTasksResolvida()
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?priority=high", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Priority.Should().ContainSingle().Which.Should().Be("High");
    }

    [Fact] // BE-22, CA-07 — repetição de prioridade
    public async Task ListTasks_PrioridadeRepetida_ChegaAoTasksComAsDuas()
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?priority=low&priority=high", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Priority.Should().Equal("Low", "High");
    }

    [Theory] // BE-22, CA-08/CA-09
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task ListTasks_OverdueIsolado_ChegaAoTasksComOValorExplicito(string overdue, bool expected)
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks?overdue={overdue}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Overdue.Should().Be(expected);
    }

    [Fact] // BE-22, RN-LIST-04 — ausente nunca chega como false explícito
    public async Task ListTasks_OverdueAusente_ChegaAoTasksSemValorExplicito()
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Overdue.Should().BeNull();
    }

    [Fact] // BE-22, CA-12/CA-13
    public async Task ListTasks_Search_ChegaAoTasksComOTexto()
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?search=relat%C3%B3rio", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Search.Should().Be("relatório");
    }

    [Theory] // BE-22, CA-17 — busca vazia ou só espaços é tratada como ausente
    [InlineData("")]
    [InlineData("%20%20")]
    public async Task ListTasks_SearchVazioOuEspacos_ChegaAoTasksComoAusente(string encodedSearch)
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks?search={encodedSearch}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Search.Should().BeEmpty();
    }

    [Fact] // BE-22, CA-10 — filtros combinados chegam todos ao Tasks na mesma chamada
    public async Task ListTasks_FiltrosCombinados_ChegamTodosAoTasks()
    {
        FakeListTasksRequest? captured = null;
        _factory.Tasks.ListTasksHandler = request =>
        {
            captured = request;
            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var client = AuthenticatedClient();

        var response = await client.GetAsync(
            new Uri("/api/tasks?status=pending&priority=high&overdue=true&search=urgente", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        captured!.Status.Should().Be("Pending");
        captured.Priority.Should().ContainSingle().Which.Should().Be("High");
        captured.Overdue.Should().BeTrue();
        captured.Search.Should().Be("urgente");
    }

    [Theory] // BE-22, CA-11 — valor inválido em qualquer filtro é 400 na borda, sem chamar o Tasks
    [InlineData("status=arquivada")]
    [InlineData("priority=urgente")]
    [InlineData("overdue=talvez")]
    public async Task ListTasks_FiltroInvalido_Retorna400SemChamarOTasks(string invalidQuery)
    {
        var callCountAntes = _factory.Tasks.ListTasksCallCount;

        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri($"/api/tasks?{invalidQuery}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.ListTasksCallCount.Should().Be(callCountAntes, "nenhuma chamada gRPC ListTasks deve ocorrer com filtro inválido");
    }

    [Fact] // BE-22, CA-11 — mesma regra de camelCase de CA-19, aplicada aos novos campos
    public async Task ListTasks_StatusInvalido_ChaveDeErrosEmCamelCase()
    {
        var client = AuthenticatedClient();

        var response = await client.GetAsync(new Uri("/api/tasks?status=arquivada", UriKind.Relative));

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\"");
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
