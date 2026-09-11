using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Allowlist explícita de CA-14: <c>/health</c>, o login e a documentação OpenAPI/Scalar (Development).</summary>
    private static bool IsAllowlisted(string routeText) =>
        routeText is "/health" or "/api/auth/login"
        || routeText.Contains("openapi", StringComparison.OrdinalIgnoreCase)
        || routeText.Contains("scalar", StringComparison.OrdinalIgnoreCase);
}
