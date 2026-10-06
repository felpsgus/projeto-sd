using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-36, CA-14 — teste de guarda de rotas: enumera os <see cref="RouteEndpoint"/>
/// mapeados e falha se qualquer rota fora da allowlist explícita de anônimos
/// permitir acesso sem autenticação, ou se alguma rota da allowlist não
/// permitir. Mesmo padrão de <c>RouteInventoryTests</c> do Tasks Service.
/// </summary>
public class RouteGuardTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public RouteGuardTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-14
    public void MapEndpoints_SoARotasDaAllowlistPermitemAcessoAnonimo()
    {
        // Força a construção do host — as rotas só existem depois disso.
        using var client = _factory.CreateClient();

        var dataSource = _factory.Services.GetRequiredService<EndpointDataSource>();
        var routeEndpoints = dataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        routeEndpoints.Should().NotBeEmpty();

        foreach (var endpoint in routeEndpoints)
        {
            var routeText = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "(rota sem nome)";
            var isAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var isAllowlisted = IsAllowlisted(routeText);

            if (isAllowlisted)
            {
                isAnonymous.Should().BeTrue(
                    $"'{routeText}' está na allowlist de rotas públicas (CA-14) e deve permitir acesso anônimo");
            }
            else
            {
                isAnonymous.Should().BeFalse(
                    $"'{routeText}' não está na allowlist (CA-14) — a fallback policy deve exigir usuário autenticado");
            }
        }
    }

    [Fact] // BE-14 CA-10 — nenhuma rota altera e-mail
    public void MapEndpoints_RotasDeEscritaSobApiMe_SaoExatamenteAsEsperadasESemCampoDeEmail()
    {
        using var client = _factory.CreateClient();

        var escritas = RouteEndpoints()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/me", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Where(m => m != HttpMethods.Get).Select(m => $"{m} {e.RoutePattern.RawText}"))
            .ToList();

        escritas.Should().BeEquivalentTo("PATCH /api/me", "POST /api/me/change-password", "DELETE /api/me");
        typeof(UpdateProfileHttpRequest).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase));
    }

    [Theory] // BE-18 CA-11 — tarefa de outro usuário (NotFound do Tasks) é 404 em todo endpoint com {id}
    [InlineData("GET", "/api/tasks/{id}")]
    [InlineData("PUT", "/api/tasks/{id}")]
    [InlineData("POST", "/api/tasks/{id}/complete")]
    [InlineData("POST", "/api/tasks/{id}/reopen")]
    [InlineData("DELETE", "/api/tasks/{id}")]
    public async Task TaskEndpointComId_TarefaDeOutroUsuario_Retorna404(string method, string template)
    {
        var naoEncontrada = new RpcException(new Status(StatusCode.NotFound, "Tarefa não encontrada."));
        _factory.Tasks.GetTaskHandler = _ => throw naoEncontrada;
        _factory.Tasks.UpdateTaskHandler = _ => throw naoEncontrada;
        _factory.Tasks.CompleteTaskHandler = _ => throw naoEncontrada;
        _factory.Tasks.ReopenTaskHandler = _ => throw naoEncontrada;
        _factory.Tasks.DeleteTaskHandler = _ => throw naoEncontrada;
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JwtTestTokens.CreateValid(_factory.SigningKey, "44444444-4444-4444-4444-444444444444"));
        using var request = new HttpRequestMessage(
            new HttpMethod(method), new Uri(template.Replace("{id}", Guid.NewGuid().ToString(), StringComparison.Ordinal), UriKind.Relative));
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new UpdateTaskHttpRequest("Título", null, "Medium", null));
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact] // BE-18 CA-11 — um endpoint novo com {id} esquecido na Theory acima quebra aqui
    public void TaskEndpointsComId_TodosEstaoCobertosPeloTesteDe404()
    {
        using var client = _factory.CreateClient();
        var cobertos = new[] { "GET /api/tasks/{id}", "PUT /api/tasks/{id}", "POST /api/tasks/{id}/complete", "POST /api/tasks/{id}/reopen", "DELETE /api/tasks/{id}" };

        var existentes = RouteEndpoints()
            .Where(e => e.RoutePattern.RawText!.Contains("{id", StringComparison.Ordinal))
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(m => $"{m} {e.RoutePattern.RawText}"));

        existentes.Should().BeEquivalentTo(cobertos);
    }

    private List<RouteEndpoint> RouteEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    /// <summary>
    /// Allowlist explícita de CA-14: <c>/health</c> (e o alias <c>/health/live</c>), login, refresh (BE-10 — o
    /// access token já pode ter expirado; o cookie é a credencial), cadastro (BE-07 —
    /// visitante sem conta precisa acessar sem token) e a documentação
    /// OpenAPI/Scalar (Development).
    /// </summary>
    private static bool IsAllowlisted(string routeText) =>
        routeText is "/health" or "/health/live" or "/api/auth/login" or "/api/auth/refresh" or "/api/auth/register"
        || routeText.Contains("openapi", StringComparison.OrdinalIgnoreCase)
        || routeText.Contains("scalar", StringComparison.OrdinalIgnoreCase);
}
