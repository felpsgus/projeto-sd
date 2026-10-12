using System.Net;
using FluentAssertions;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-24, CA-07 — <c>/health/live</c> é alias anônimo de <c>/health</c>, sem depender de Identity/Tasks.</summary>
public class HealthLiveTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public HealthLiveTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task GetHealth_SemTokenEComBackendsSemConfigurar_RetornaOk(string path)
    {
        var response = await _factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
