using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TodoList.Identity.Infrastructure.Persistence;
using TodoList.Identity.IntegrationTests.Persistence;
using Xunit;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// A outra metade do CA-04 de BE-02. <see cref="HealthEndpointTests"/> prova que
/// <c>/health/ready</c> fica degradado com o banco fora do ar — mas isso sozinho
/// não distingue um readiness correto de um que nunca fica pronto: um check
/// quebrado passaria naquele teste igual. Aqui o Postgres está de pé
/// (Testcontainers) e o mesmo endpoint precisa responder 200.
/// <b>Requer Docker</b> (<c>Category=Docker</c>).
/// </summary>
[Collection("Postgres")]
public class HealthReadyComBancoRealTests : IAsyncLifetime
{
    private readonly PostgresContainerFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;

    public HealthReadyComBancoRealTests(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.EnsureStartedAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] = _fixture.ConnectionString,
                })));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact] // CA-04
    [Trait("Category", "Docker")]
    public async Task GetHealthReady_ComPostgresDePe_RespondeOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "com o banco alcançável o readiness precisa ficar pronto — sem este teste, um readiness que nunca "
            + "fica pronto passaria igual no teste de banco fora do ar (CA-04)");
    }
}
