using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace TodoList.Identity.IntegrationTests;

/// <summary>BE-01, CA-04 — em Development a UI de OpenAPI está acessível e o documento lista o health; fora dele, não é servida.</summary>
public class OpenApiUiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiUiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Development_DocumentoListaOHealthEAUiResponde200()
    {
        var client = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient();

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
        var client = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        (await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
