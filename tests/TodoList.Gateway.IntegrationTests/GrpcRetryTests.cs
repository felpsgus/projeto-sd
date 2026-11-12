// Ver o comentário de GatewayApiFactory.cs — extern alias evita a colisão de
// nomes entre o lado cliente do proto e o lado servidor dos dublês.
extern alias Fakes;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using FakeListTasksReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeListTasksReply;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-43 (D-44) — retry nativo do Grpc.Net.Client só nas RPCs de leitura, só em <c>Unavailable</c>.</summary>
public class GrpcRetryTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public GrpcRetryTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    private static RpcException Unavailable() => new(new Status(StatusCode.Unavailable, string.Empty));

    [Fact] // CA-01
    public async Task ListTasks_UnavailableUmaVez_RetentaERetorna200()
    {
        var antes = _factory.Tasks.ListTasksCallCount;
        var falhou = false;
        _factory.Tasks.ListTasksHandler = request =>
        {
            if (!falhou)
            {
                falhou = true;
                throw Unavailable();
            }

            return new FakeListTasksReply([], request.Page, request.PageSize, 0);
        };

        var response = await AuthenticatedClient().GetAsync(new Uri("/api/tasks", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (_factory.Tasks.ListTasksCallCount - antes).Should().Be(2);
    }

    [Fact] // CA-02
    public async Task ListTasks_SempreUnavailable_ParaEmTresTentativasERetorna503()
    {
        var antes = _factory.Tasks.ListTasksCallCount;
        _factory.Tasks.ListTasksHandler = _ => throw Unavailable();

        var response = await AuthenticatedClient().GetAsync(new Uri("/api/tasks", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.Contains("Retry-After").Should().BeTrue();
        (_factory.Tasks.ListTasksCallCount - antes).Should().Be(3);
    }

    [Fact] // CA-03
    public async Task CreateTask_Unavailable_NaoRetentaERetorna503()
    {
        var antes = _factory.Tasks.CreateTaskCallCount;
        _factory.Tasks.CreateTaskHandler = _ => throw Unavailable();

        var response = await AuthenticatedClient().PostAsJsonAsync(
            new Uri("/api/tasks", UriKind.Relative), new CreateTaskHttpRequest("Comprar leite", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (_factory.Tasks.CreateTaskCallCount - antes).Should().Be(1);
    }

    [Fact] // CA-04
    public async Task GetProfile_UnavailableUmaVez_RetentaERetorna200()
    {
        var antes = _factory.Identity.GetProfileCallCount;
        var falhou = false;
        _factory.Identity.GetProfileHandler = userId =>
        {
            if (!falhou)
            {
                falhou = true;
                throw Unavailable();
            }

            return (userId, "a@example.com", "Ana", DateTimeOffset.UtcNow);
        };

        var response = await AuthenticatedClient().GetAsync(new Uri("/api/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (_factory.Identity.GetProfileCallCount - antes).Should().Be(2);
    }

    [Fact] // CA-05
    public async Task Register_Unavailable_NaoRetenta()
    {
        var antes = _factory.Identity.RegisterCallCount;
        _factory.Identity.RegisterHandler = (_, _, _) => throw Unavailable();

        var response = await _factory.CreateClient().PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative), new RegisterHttpRequest("nova@example.com", "senha123", null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (_factory.Identity.RegisterCallCount - antes).Should().Be(1);
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
