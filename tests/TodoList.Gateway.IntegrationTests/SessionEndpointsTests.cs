using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// Sessão no Gateway (BE-09/BE-10/BE-11, D-20): o cookie <c>refreshToken</c>
/// (atributos, entrega e remoção), refresh só pelo cookie, 401 único, logout e
/// logout-all autenticados e idempotentes, <c>Cache-Control: no-store</c>. O
/// Identity é o dublê <see cref="GatewayApiFactory.Identity"/>.
/// </summary>
public class SessionEndpointsTests : IClassFixture<GatewayApiFactory>
{
    private const string UserId = "44444444-4444-4444-4444-444444444444";
    private const string CookieValue = "valor-do-cookie-atual";
    private const string RefreshUrl = "/api/auth/refresh";

    private readonly GatewayApiFactory _factory;

    public SessionEndpointsTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // BE-09 CA-01, CA-01b, CA-01c, CA-01d, CA-13
    public async Task Login_Sucesso_EmiteCookieHttpOnlyComAtributosECorpoSemRefreshToken()
    {
        _factory.Identity.LoginRefreshToken = "refresh-do-login";
        _factory.Identity.LoginRefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        _factory.Identity.LoginHandler = (_, _) => (true, "access-do-login", DateTimeOffset.UtcNow.AddMinutes(15), UserId);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = SetCookies(response).Should().ContainSingle().Subject;
        cookie.Should().StartWith("refreshToken=refresh-do-login;");
        AssertCookieAttributes(cookie, secure: true);
        MaxAgeSeconds(cookie).Should().BeInRange(7 * 86400 - 120, 7 * 86400);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("refresh-do-login").And.NotContainEquivalentOf("refresh");
        body.Should().Contain("access-do-login");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact] // BE-09 CA-01d: o Max-Age sai da expiração do Identity (= Jwt:RefreshTokenDays), não de constante do Gateway
    public async Task Login_MaxAgeDoCookieAcompanhaAExpiracaoDoRefreshToken()
    {
        _factory.Identity.LoginRefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(2);
        _factory.Identity.LoginHandler = (_, _) => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), UserId);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));

        MaxAgeSeconds(SetCookies(response).Single()).Should().BeInRange(2 * 86400 - 120, 2 * 86400);
    }

    [Fact] // BE-09 CA-10b: falha de login nunca cria sessão nem cookie; CA-13 vale também para o erro
    public async Task Login_Falha_NaoEmiteCookie()
    {
        _factory.Identity.LoginHandler = (_, _) => (false, string.Empty, default, string.Empty);

        var response = await _factory.CreateClient().PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "errada"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SetCookies(response).Should().BeEmpty();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact] // BE-10 CA-01, CA-03, CA-03b, CA-04, CA-18b, CA-21
    public async Task Refresh_CookieValido_Retorna200ComNovoParERotacionaOCookie()
    {
        _factory.Identity.RefreshSessionHandler = token => (
            true, "access-novo", DateTimeOffset.UtcNow.AddMinutes(15), "cookie-rotacionado", DateTimeOffset.UtcNow.AddDays(7));

        var response = await RefreshAsync(cookie: CookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Identity.LastRefreshSessionToken.Should().Be(CookieValue);
        var body = await response.Content.ReadFromJsonAsync<LoginHttpResponse>();
        body!.AccessToken.Should().Be("access-novo");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("cookie-rotacionado");
        var cookie = SetCookies(response).Should().ContainSingle().Subject;
        cookie.Should().StartWith("refreshToken=cookie-rotacionado;");
        AssertCookieAttributes(cookie, secure: true);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact] // BE-10 CA-05: access token anterior já expirado no cabeçalho não atrapalha (rota anônima)
    public async Task Refresh_ComAccessTokenExpiradoNoCabecalho_FuncionaMesmoAssim()
    {
        _factory.Identity.RefreshSessionHandler = _ => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), "novo", DateTimeOffset.UtcNow.AddDays(7));
        var request = RefreshRequest(CookieValue);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateExpired(_factory.SigningKey, UserId));

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] // BE-10 CA-03c: o cookie é o único caminho de entrada — corpo, query e header customizado não autenticam
    public async Task Refresh_TokenForaDoCookie_Retorna401SemChamarOIdentity()
    {
        _factory.Identity.RefreshSessionHandler = _ => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), "novo", DateTimeOffset.UtcNow.AddDays(7));
        var before = _factory.Identity.RefreshSessionCallCount;
        var client = _factory.CreateClient();

        var byBody = await client.PostAsJsonAsync(new Uri(RefreshUrl, UriKind.Relative), new { refreshToken = CookieValue });
        var byQuery = await client.PostAsync(new Uri($"{RefreshUrl}?refreshToken={CookieValue}", UriKind.Relative), content: null);
        var byHeader = new HttpRequestMessage(HttpMethod.Post, RefreshUrl);
        byHeader.Headers.Add("X-Refresh-Token", CookieValue);

        foreach (var response in new[] { byBody, byQuery, await client.SendAsync(byHeader) })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        _factory.Identity.RefreshSessionCallCount.Should().Be(before);
    }

    [Fact] // BE-10 CA-16, CA-18c: toda falha é o mesmo 401, com o mesmo corpo, e apaga o cookie
    public async Task Refresh_QualquerFalha_Retorna401IdenticoEApagaOCookie()
    {
        _factory.Identity.RefreshSessionHandler = _ => (false, string.Empty, default, string.Empty, default);

        var semCookie = await RefreshAsync(cookie: null);
        var tokenInvalido = await RefreshAsync(cookie: "token-morto");

        foreach (var response in new[] { semCookie, tokenInvalido })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var cookie = SetCookies(response).Should().ContainSingle().Subject;
            AssertDeletedCookie(cookie, secure: true);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }

        var bodies = new[] { await NormalizedBodyAsync(semCookie), await NormalizedBodyAsync(tokenInvalido) };
        bodies[0].Should().Be(bodies[1], "o cliente não distingue ausente de inválido");
        bodies[0].Should().Contain("auth.invalid_refresh_token");
    }

    [Fact] // FE-06 CA-12, RN-AUTH-19: revogado por ação do usuário é 401 com código próprio e apaga o cookie
    public async Task Refresh_TokenRevogadoPeloUsuario_Retorna401ComCodigoDeRevogacaoEApagaOCookie()
    {
        _factory.Identity.RefreshSessionHandler = _ => (false, string.Empty, default, string.Empty, default);
        _factory.Identity.RefreshSessionRevoked = true;

        try
        {
            var response = await RefreshAsync(cookie: CookieValue);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            AssertDeletedCookie(SetCookies(response).Should().ContainSingle().Subject, secure: true);
            (await NormalizedBodyAsync(response)).Should().Contain("auth.refresh_token_revoked");
        }
        finally
        {
            _factory.Identity.RefreshSessionRevoked = false;
        }
    }

    [Fact] // D-28: indisponibilidade é 503 e NÃO apaga o cookie — a sessão ainda pode estar viva
    public async Task Refresh_IdentityIndisponivel_Retorna503SemApagarOCookie()
    {
        _factory.Identity.RefreshSessionHandler = _ => throw new RpcException(new Status(StatusCode.Unavailable, "fora do ar"));

        var response = await RefreshAsync(cookie: CookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        SetCookies(response).Should().BeEmpty();
    }

    [Fact] // BE-11 CA-01, CA-01b, CA-08, CA-12: user_id vem do token, token vem do cookie, 204, cookie apagado
    public async Task Logout_Autenticado_Retorna204RevogaPeloCookieEApagaOCookie()
    {
        _factory.Identity.LogoutHandler = (_, _) => { };

        var response = await AuthenticatedAsync("/api/auth/logout", cookie: CookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Identity.LastLogoutUserId.Should().Be(UserId);
        _factory.Identity.LastLogoutRefreshToken.Should().Be(CookieValue);
        AssertDeletedCookie(SetCookies(response).Should().ContainSingle().Subject, secure: true);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact] // BE-11 CA-08: sem cookie é idempotente — 204, cookie apagado, Identity nem é chamado
    public async Task Logout_SemCookie_Retorna204SemChamarOIdentity()
    {
        var before = _factory.Identity.LogoutCallCount;

        var response = await AuthenticatedAsync("/api/auth/logout", cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        AssertDeletedCookie(SetCookies(response).Should().ContainSingle().Subject, secure: true);
        _factory.Identity.LogoutCallCount.Should().Be(before);
    }

    [Theory] // BE-11 CA-07: sem autenticação é 401
    [InlineData("/api/auth/logout")]
    [InlineData("/api/auth/logout-all")]
    public async Task LogoutOuLogoutAll_SemAutenticacao_Retorna401(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"refreshToken={CookieValue}");

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-11 CA-06
    public async Task LogoutAll_Autenticado_Retorna204ApagaOCookieEUsaOUserIdDoToken()
    {
        _factory.Identity.LogoutAllHandler = _ => { };

        var response = await AuthenticatedAsync("/api/auth/logout-all", cookie: CookieValue);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _factory.Identity.LastLogoutAllUserId.Should().Be(UserId);
        AssertDeletedCookie(SetCookies(response).Should().ContainSingle().Subject, secure: true);
    }

    [Fact] // BE-11 CA-11 / D-41: o access token emitido antes do logout continua valendo até expirar
    public async Task AccessTokenEmitidoAntesDoLogout_ContinuaAceitoAteExpirar()
    {
        _factory.Identity.LogoutHandler = (_, _) => { };
        _factory.Identity.GetProfileHandler = id => (id, "ada@example.com", "Ada", DateTimeOffset.UtcNow);
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, UserId);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        logout.Headers.Add("Cookie", $"refreshToken={CookieValue}");
        (await _factory.CreateClient().SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var profile = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        profile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _factory.CreateClient().SendAsync(profile);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "decisão D-41: logout não invalida o JWT; a janela é a validade do access token");
    }

    [Fact] // BE-11 CA-12 / RN-AUTH-20: o refresh token nunca vai para log nem para o corpo
    public async Task RefreshToken_NuncaApareceNosLogs()
    {
        const string secret = "segredo-que-nao-pode-vazar-no-log";
        _factory.Identity.LoginRefreshToken = secret;
        _factory.Identity.LoginHandler = (_, _) => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), UserId);
        _factory.Identity.RefreshSessionHandler = _ => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), secret + "-novo", DateTimeOffset.UtcNow.AddDays(7));

        await _factory.CreateClient().PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));
        await RefreshAsync(cookie: secret);

        _factory.Logs.Entries.Should().OnlyContain(entry => !entry.Contains(secret, StringComparison.Ordinal));
    }

    [Fact] // D-42: RefreshCookie:Secure=false (deploy HTTP puro) tira só o Secure — emissão e remoção continuam com atributos idênticos
    public async Task SecureFalse_TiraSoOAtributoSecureNaEmissaoENaRemocao()
    {
        using var insecureFactory = CreateInsecureCookieFactory();
        insecureFactory.Identity.LoginHandler = (_, _) => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), UserId);
        insecureFactory.Identity.RefreshSessionHandler = _ => (false, string.Empty, default, string.Empty, default);

        var login = await insecureFactory.CreateClient().PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));
        var refresh = await insecureFactory.CreateClient().SendAsync(RefreshRequest(CookieValue));

        AssertCookieAttributes(SetCookies(login).Single(), secure: false);
        AssertDeletedCookie(SetCookies(refresh).Single(), secure: false);
    }

    [Fact] // BE-11 CA-13: com um cookie jar de verdade, o logout faz o navegador parar de enviar o cookie
    public async Task Logout_ComCookieJarReal_OClienteDeixaDeEnviarOCookie()
    {
        // Secure=false só porque o TestServer é http://localhost e o CookieContainer do .NET
        // (como o navegador de uma VM HTTP) não envia cookie Secure por HTTP.
        using var factory = CreateInsecureCookieFactory();
        factory.Identity.LoginRefreshToken = "cookie-do-jar";
        factory.Identity.LoginHandler = (_, _) => (true, "access", DateTimeOffset.UtcNow.AddMinutes(15), UserId);
        factory.Identity.LogoutHandler = (_, _) => { };
        factory.Identity.RefreshSessionHandler = _ => (false, string.Empty, default, string.Empty, default);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));
        await client.PostAsync(new Uri(RefreshUrl, UriKind.Relative), content: null);
        factory.Identity.LastRefreshSessionToken.Should().Be("cookie-do-jar", "o jar enviou o cookie sozinho, em /api/auth/*");

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateValid(factory.SigningKey, UserId));
        (await client.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var callsBefore = factory.Identity.RefreshSessionCallCount;
        var afterLogout = await client.PostAsync(new Uri(RefreshUrl, UriKind.Relative), content: null);

        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        factory.Identity.RefreshSessionCallCount.Should().Be(callsBefore, "sem cookie o Gateway nem consulta o Identity");
    }

    /// <summary>Instância própria (chave RSA própria) com <c>RefreshCookie:Secure=false</c> — ver <see cref="GatewayApiFactory.AdditionalSettings"/>.</summary>
    private static GatewayApiFactory CreateInsecureCookieFactory() =>
        new() { AdditionalSettings = { ["RefreshCookie:Secure"] = "false" } };

    private static string WithoutTraceId(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("traceId");

        return node.ToJsonString();
    }

    private static async Task<string> NormalizedBodyAsync(HttpResponseMessage response) =>
        WithoutTraceId(await response.Content.ReadAsStringAsync());

    private static HttpRequestMessage RefreshRequest(string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, RefreshUrl);

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"refreshToken={cookie}");
        }

        return request;
    }

    private Task<HttpResponseMessage> RefreshAsync(string? cookie) => _factory.CreateClient().SendAsync(RefreshRequest(cookie));

    private Task<HttpResponseMessage> AuthenticatedAsync(string url, string? cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JwtTestTokens.CreateValid(_factory.SigningKey, UserId));

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"refreshToken={cookie}");
        }

        return _factory.CreateClient().SendAsync(request);
    }

    private static List<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    /// <summary>CA-01c/CA-18b: HttpOnly, SameSite=Strict e Path=/api/auth sempre; Secure conforme a configuração.</summary>
    private static void AssertCookieAttributes(string cookie, bool secure)
    {
        var attributes = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToList();

        attributes.Should().Contain("httponly").And.Contain("samesite=strict").And.Contain("path=/api/auth");
        attributes.Contains("secure").Should().Be(secure);
    }

    /// <summary>
    /// A remoção só apaga o cookie original se Path/SameSite/Secure/HttpOnly forem
    /// idênticos aos da emissão (BE-11 CA-01b) — e se o valor é vazio e a data, passada.
    /// </summary>
    private static void AssertDeletedCookie(string cookie, bool secure)
    {
        cookie.Should().StartWith("refreshToken=;");
        AssertCookieAttributes(cookie, secure);
        var expires = cookie.Split(';', StringSplitOptions.TrimEntries)
            .Single(attribute => attribute.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];
        DateTimeOffset.Parse(expires, System.Globalization.CultureInfo.InvariantCulture).Should().BeBefore(DateTimeOffset.UtcNow);
    }

    private static int MaxAgeSeconds(string cookie) =>
        int.Parse(
            cookie.Split(';', StringSplitOptions.TrimEntries)
                .Single(attribute => attribute.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))["max-age=".Length..],
            System.Globalization.CultureInfo.InvariantCulture);
}
