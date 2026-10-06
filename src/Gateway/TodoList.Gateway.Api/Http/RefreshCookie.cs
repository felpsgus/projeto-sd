using Microsoft.Extensions.Options;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Http;

/// <summary>
/// Único lugar que lê, escreve e apaga o cookie <c>refreshToken</c> (D-20).
/// Escrita e remoção montam as opções pelo <b>mesmo</b> método: se
/// <c>Path</c>/<c>SameSite</c>/<c>Secure</c> divergissem entre as duas, o
/// navegador trataria o <c>Set-Cookie</c> de expiração como um segundo cookie e
/// o logout "não funcionaria às vezes" (nota técnica de BE-11).
/// </summary>
public sealed class RefreshCookie
{
    public const string Name = "refreshToken";

    /// <summary>Só as rotas <c>/api/auth/*</c> recebem o cookie — ele não viaja em toda requisição da API.</summary>
    public const string CookiePath = "/api/auth";

    private readonly RefreshCookieOptions _options;
    private readonly TimeProvider _timeProvider;

    public RefreshCookie(IOptions<RefreshCookieOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>Valor do cookie, ou <c>null</c> se ausente/vazio. É o único caminho de entrada do refresh token (CA-03c de BE-10).</summary>
    public string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    /// <summary>Emite o cookie; o <c>Max-Age</c> sai da expiração informada pelo Identity (= <c>Jwt:RefreshTokenDays</c>).</summary>
    public void Write(HttpResponse response, string value, DateTimeOffset expiresAt)
    {
        var options = BuildOptions();
        var remaining = expiresAt - _timeProvider.GetUtcNow();
        options.MaxAge = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;

        response.Cookies.Append(Name, value, options);
    }

    /// <summary>Apaga o cookie com os mesmos atributos da emissão.</summary>
    public void Delete(HttpResponse response) => response.Cookies.Delete(Name, BuildOptions());

    private CookieOptions BuildOptions() => new()
    {
        HttpOnly = true,
        Secure = _options.Secure,
        SameSite = SameSiteMode.Strict,
        Path = CookiePath,
    };
}
