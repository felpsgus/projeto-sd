using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-01, CA-04 — em Development a UI de OpenAPI está acessível e o documento lista o health; fora dele, não é servida.</summary>
public class OpenApiUiTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public OpenApiUiTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Development_DocumentoListaOHealthEAUiResponde200()
    {
        var client = _factory.CreateClient();

        var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        document.StatusCode.Should().Be(HttpStatusCode.OK);
        var paths = JsonDocument.Parse(await document.Content.ReadAsStringAsync()).RootElement.GetProperty("paths");
        paths.TryGetProperty("/health", out _).Should().BeTrue("o health precisa aparecer no documento OpenAPI");

        var ui = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative));
        ui.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForaDeDevelopment_DocumentoEUiNaoSaoServidos()
    {
        using var production = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient();

        // Rota não mapeada: 404, ou 401 quando a política de autorização padrão do Gateway responde antes do roteamento.
        (await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative))).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
        (await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative))).StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
    }
}
