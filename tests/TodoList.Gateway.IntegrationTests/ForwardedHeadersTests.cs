using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TodoList.Gateway.Api.Http;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-42 — <c>AddGatewayForwardedHeaders</c>/<c>UseForwardedHeaders</c>: um
/// <c>X-Forwarded-For</c> vindo do loopback (o nginx, na VM, o único proxy
/// confiável depois desta task) é respeitado, atualizando
/// <c>HttpContext.Connection.RemoteIpAddress</c> para o IP do cliente real; o
/// mesmo header vindo de um endereço não confiável é ignorado — sem essa
/// restrição, qualquer chamador que alcançasse o Gateway diretamente poderia
/// forjar a própria origem aparente. Hospeda só o middleware sob teste (não o
/// <see cref="GatewayApiFactory"/> inteiro) porque o único comportamento em
/// jogo é o pipeline ASP.NET Core em si, independente de autenticação/backends.
/// </summary>
public class ForwardedHeadersTests
{
    [Fact]
    public async Task XForwardedFor_DoLoopback_AtualizaRemoteIpAddressParaOClienteReal()
    {
        using var host = await BuildHostAsync(connectionRemoteIpAddress: IPAddress.Loopback);
        using var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be("203.0.113.7", "o loopback é o único proxy confiável (o nginx) e seu X-Forwarded-For deve ser respeitado");
    }

    [Fact]
    public async Task XForwardedFor_DeOrigemNaoLoopback_EIgnorado()
    {
        var origemNaoConfiavel = IPAddress.Parse("203.0.113.99");
        using var host = await BuildHostAsync(connectionRemoteIpAddress: origemNaoConfiavel);
        using var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-For", "198.51.100.1");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().Be(
            origemNaoConfiavel.ToString(),
            "KnownProxies restrito ao loopback significa que um X-Forwarded-For vindo de fora dele não pode forjar a origem aparente");
    }

    /// <summary>
    /// Monta um host mínimo (sem o restante do Program.cs do Gateway) com só
    /// <c>AddGatewayForwardedHeaders</c>/<c>UseForwardedHeaders</c> à frente de
    /// um endpoint terminal que devolve o
    /// <c>HttpContext.Connection.RemoteIpAddress</c> resultante — o mesmo
    /// padrão de configuração e a mesma ordem de pipeline usados em
    /// <c>Program.cs</c> real (ForwardedHeaders antes de qualquer outra
    /// coisa), sem nenhum appsettings explícito: exercita o valor padrão de
    /// <c>ForwardedHeaders:KnownProxies</c> (só loopback).
    /// </summary>
    private static async Task<IHost> BuildHostAsync(IPAddress connectionRemoteIpAddress)
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                        services.AddGatewayForwardedHeaders(new ConfigurationBuilder().Build()))
                    .Configure(app =>
                    {
                        // Simula a conexão TCP chegando do nginx (ou de um
                        // chamador direto, no teste de origem não confiável) —
                        // TestServer não tem um socket real, então o IP
                        // "de rede" precisa ser atribuído explicitamente antes
                        // de UseForwardedHeaders decidir se confia nele.
                        app.Use((context, next) =>
                        {
                            context.Connection.RemoteIpAddress = connectionRemoteIpAddress;
                            return next();
                        });

                        app.UseForwardedHeaders();

                        app.Run(context =>
                            context.Response.WriteAsync(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty));
                    });
            });

        return await hostBuilder.StartAsync();
    }
}
