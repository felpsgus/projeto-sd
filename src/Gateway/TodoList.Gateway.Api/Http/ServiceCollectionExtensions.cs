using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Http;

/// <summary>
/// Fiação de <c>ForwardedHeadersOptions</c> (BE-42) — o Gateway, na VM, passa
/// a escutar só em <c>127.0.0.1:8080</c> e a receber tráfego exclusivamente do
/// nginx (mesma máquina); sem <c>UseForwardedHeaders</c>, toda requisição
/// chegaria com <c>HttpContext.Connection.RemoteIpAddress</c> igual ao do
/// nginx (127.0.0.1), não ao do cliente real, degradando qualquer log ou
/// decisão futura baseada em IP de origem.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ForwardedHeadersConfigurationOptions>()
            .Bind(configuration.GetSection(ForwardedHeadersConfigurationOptions.SectionName));

        // Configure<TDep> (em vez de passar o Action direto ao
        // UseForwardedHeaders) porque a lista de KnownProxies vem de
        // configuração, resolvida por DI — o UseForwardedHeaders() sem
        // parâmetro lê IOptions<ForwardedHeadersOptions> do container no
        // momento em que o pipeline é montado (fail-fast: um IP inválido em
        // ForwardedHeaders:KnownProxies derruba a inicialização, não a
        // primeira requisição).
        services
            .AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersConfigurationOptions>>((forwardedHeadersOptions, configurationOptions) =>
            {
                forwardedHeadersOptions.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // KnownIPNetworks/KnownProxies do framework já trazem o
                // loopback por padrão — limpos aqui para que a lista efetiva
                // seja exatamente a de ForwardedHeaders:KnownProxies (nunca a
                // união implícita com o default do framework), deixando
                // explícito de onde vem cada proxy confiável.
                forwardedHeadersOptions.KnownIPNetworks.Clear();
                forwardedHeadersOptions.KnownProxies.Clear();

                foreach (var proxy in configurationOptions.Value.KnownProxies)
                {
                    if (!IPAddress.TryParse(proxy, out var address))
                    {
                        throw new InvalidOperationException(
                            $"A chave de configuração 'ForwardedHeaders:KnownProxies' contém um endereço IP inválido: '{proxy}'.");
                    }

                    forwardedHeadersOptions.KnownProxies.Add(address);
                }
            });

        return services;
    }
}
