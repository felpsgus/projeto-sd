// Ver o comentário de GatewayApiFactory.cs — extern alias evita a colisão de
// nomes entre o lado cliente do proto (TodoList.Gateway.Api) e o lado
// servidor (TodoList.Gateway.IntegrationTests.Fakes), ambos no mesmo
// csharp_namespace.
extern alias Fakes;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;
using FakeTaskReply = Fakes::TodoList.Gateway.IntegrationTests.Fakes.FakeTaskReply;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-20 — <c>POST /api/tasks/{id}/complete</c> e <c>POST /api/tasks/{id}/reopen</c>: sucesso, 401, 400 de id malformado, 404 de tarefa alheia/inexistente, 409 de transição inválida.</summary>
public class CompleteReopenTaskTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "22222222-2222-2222-2222-222222222222";

    private readonly GatewayApiFactory _factory;

    public CompleteReopenTaskTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteTask_TarefaPropriaPendente_Retorna200ComStatusCompleted()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.CompleteTaskHandler = id => new FakeTaskReply(id, "Tarefa", null, "Medium", "Completed", null, false, now, now, now);

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{taskId}/complete", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Id.Should().Be(taskId);
        body.Status.Should().Be("Completed");
        body.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReopenTask_TarefaPropriaConcluida_Retorna200ComStatusPending()
    {
        var taskId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        _factory.Tasks.ReopenTaskHandler = id => new FakeTaskReply(id, "Tarefa", null, "Medium", "Pending", null, false, null, now, now);

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{taskId}/reopen", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TaskHttpResponse>();
        body!.Id.Should().Be(taskId);
        body.Status.Should().Be("Pending");
        body.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task CompleteTask_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/complete", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReopenTask_SemToken_Retorna401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/reopen", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteTask_IdNaoEhGuid_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.CompleteTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri("/api/tasks/abc/complete", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.CompleteTaskCallCount.Should().Be(callCountAntes, "id malformado não deve gerar chamada gRPC CompleteTask");
    }

    [Fact]
    public async Task ReopenTask_IdNaoEhGuid_Retorna400SemChamarOTasks()
    {
        var callCountAntes = _factory.Tasks.ReopenTaskCallCount;

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri("/api/tasks/abc/reopen", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Tasks.ReopenTaskCallCount.Should().Be(callCountAntes, "id malformado não deve gerar chamada gRPC ReopenTask");
    }

    [Fact]
    public async Task CompleteTask_TarefaAlheiaOuInexistente_Retorna404()
    {
        _factory.Tasks.CompleteTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/complete", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReopenTask_TarefaAlheiaOuInexistente_Retorna404()
    {
        _factory.Tasks.ReopenTaskHandler = _ => throw new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/reopen", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // D-35 — FailedPrecondition (transição inválida) vira 409, com o errorCode do catálogo preservado
    public async Task CompleteTask_JaConcluida_Retorna409ComTaskAlreadyCompleted()
    {
        _factory.Tasks.CompleteTaskHandler = _ =>
        {
            var trailers = new Metadata { { "error-code", "task.already_completed" } };
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Conflito de estado."), trailers);
        };

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/complete", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("task.already_completed");
    }

    [Fact] // D-35 — FailedPrecondition (transição inválida) vira 409, com o errorCode do catálogo preservado
    public async Task ReopenTask_AindaPendente_Retorna409ComTaskNotCompleted()
    {
        _factory.Tasks.ReopenTaskHandler = _ =>
        {
            var trailers = new Metadata { { "error-code", "task.not_completed" } };
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Conflito de estado."), trailers);
        };

        var client = AuthenticatedClient();

        var response = await client.PostAsync(new Uri($"/api/tasks/{Guid.NewGuid()}/reopen", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("task.not_completed");
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
