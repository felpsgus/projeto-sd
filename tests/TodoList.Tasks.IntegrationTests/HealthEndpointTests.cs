using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TodoList.Tasks.Infrastructure.Persistence;
using Xunit;

namespace TodoList.Tasks.IntegrationTests;

/// <summary>
/// CA-03, CA-04 de BE-02: <c>/health</c> (liveness) nunca depende de banco;
/// <c>/health/ready</c> (readiness) reporta degradado quando o Postgres está
/// fora do ar — sem derrubar o processo. O teste de "banco fora do ar" usa
/// uma connection string sintaticamente válida apontando para uma porta
/// fechada em loopback — falha de conexão determinística, sem precisar de
/// Docker/Postgres real (a cobertura com Postgres real fica nos testes
/// Testcontainers marcados como skip).
/// </summary>
public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ServiceCollectionExtensions.ConnectionStringName}"] =
                        "Host=127.0.0.1;Port=1;Database=todolist;Username=postgres;Password=postgres;Timeout=2",
                });
            });
        });
    }

    [Fact] // CA-04
    public async Task GetHealth_RetornaOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] // CA-03, CA-04
    public async Task GetHealthReady_ComBancoIndisponivel_RespondeDegradadoSemDerrubarOProcesso()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // O processo continua de pé: o liveness check ainda responde normalmente.
        var liveResponse = await client.GetAsync(new Uri("/health", UriKind.Relative));
        liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
