namespace TodoList.Gateway.Api.Configuration;

/// <summary>
/// Seção <c>ForwardedHeaders</c> do Gateway (BE-42) — lista de proxies
/// confiáveis para <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c>. Vem de
/// configuração (não de constante fixa no código) para o T3 poder ajustar a
/// lista sem recompilar, mas o padrão já é o loopback (<c>127.0.0.1</c>,
/// <c>::1</c>) — o único lugar de onde o Gateway aceita conexão depois de
/// BE-42 (o nginx, na mesma máquina). Nunca deixar esta lista aberta: sem um
/// <c>KnownProxies</c> restrito, qualquer chamador que alcançasse a porta do
/// Gateway diretamente poderia forjar a própria origem aparente via
/// <c>X-Forwarded-For</c>.
/// </summary>
public sealed class ForwardedHeadersConfigurationOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Endereços IP (texto, formato aceito por <see cref="System.Net.IPAddress.TryParse(string, out System.Net.IPAddress)"/>)
    /// dos proxies confiáveis. Padrão: só loopback.
    /// </summary>
    public IReadOnlyList<string> KnownProxies { get; init; } = ["127.0.0.1", "::1"];
}
