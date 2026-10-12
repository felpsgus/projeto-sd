using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-24 — Gateway real + Identity real (Postgres) em processo, com o log em <c>Verbose</c>:
/// CA-04 (nada sensível no log de cadastro, login, refresh, troca de senha e logout), CA-02/CA-03/CA-07b/CA-07c
/// (<c>traceId</c> idêntico no log dos dois serviços e no <c>ProblemDetails</c>; <c>userId</c> só em requisição
/// autenticada; campo <c>service</c>). <b>Requer Docker.</b>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LogLeakageTests : IClassFixture<RealChainFixture>
{
    private const string Password = "Senha-Original-7391";
    private const string NewPassword = "Senha-Nova-Troca-5528";
    private const string WrongPassword = "Senha-Errada-Marcador-4410";

    private readonly RealChainFixture _chain;

    public LogLeakageTests(RealChainFixture chain)
    {
        _chain = chain;
    }

    [Fact] // CA-04 — entregável central da task
    public async Task FluxoCompleto_NenhumSegredoApareceEmNenhumLogDosDoisServicos()
    {
        var email = $"vazamento-{Guid.NewGuid():N}@example.com";
        var secrets = new Dictionary<string, string>
        {
            ["senha"] = Password,
            ["nova senha"] = NewPassword,
            ["senha errada"] = WrongPassword,
        };
        using var client = _chain.CreateGatewayClient();

        // 1) cadastro
        (await Post(client, "/api/auth/register", new RegisterHttpRequest(email, Password, "Fulano de Tal")))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var hashAfterRegister = await _chain.ReadPasswordHashAsync(email);
        secrets["hash da senha (cadastro)"] = hashAfterRegister;

        // 2) login com senha errada (o valor digitado também não pode ir para o log) e depois certo
        (await Post(client, "/api/auth/login", new LoginHttpRequest(email, WrongPassword)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await Login(client, email, Password);
        secrets["access token (login)"] = login.AccessToken;
        secrets["refresh token (login)"] = login.RefreshToken;

        // 3) refresh, só pelo cookie
        var refresh = await Refresh(client, login.RefreshToken);
        secrets["access token (refresh)"] = refresh.AccessToken;
        secrets["refresh token (refresh)"] = refresh.RefreshToken;

        // 4) troca de senha
        using (var change = new HttpRequestMessage(HttpMethod.Post, "/api/me/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordHttpRequest(Password, NewPassword)),
        })
        {
            change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refresh.AccessToken);
            (await client.SendAsync(change)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var hashAfterChange = await _chain.ReadPasswordHashAsync(email);
        hashAfterChange.Should().NotBe(hashAfterRegister);
        secrets["hash da senha (troca)"] = hashAfterChange;

        // 5) novo login e logout
        var secondLogin = await Login(client, email, NewPassword);
        secrets["access token (2º login)"] = secondLogin.AccessToken;
        secrets["refresh token (2º login)"] = secondLogin.RefreshToken;

        var logoutTraceId = Guid.NewGuid().ToString("N");
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
        {
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondLogin.AccessToken);
            logout.Headers.Add("Cookie", $"refreshToken={secondLogin.RefreshToken}");
            logout.Headers.Add("traceparent", $"00-{logoutTraceId}-0123456789abcdef-01");
            (await client.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // O log de requisição sai depois da resposta: espera o do logout (a última chamada) antes de varrer tudo.
        await EntryAsync(_chain.GatewayLog, logoutTraceId, "/api/auth/logout");

        // Anti-vácuo: o log foi mesmo capturado em nível baixo, nos dois serviços, inclusive o EF Core.
        var all = _chain.AllLogLines;
        all.Should().Contain(line => line.Contains("\"service\":\"gateway\"", StringComparison.Ordinal));
        all.Should().Contain(line => line.Contains("\"service\":\"identity\"", StringComparison.Ordinal));
        all.Should().Contain(
            line => line.Contains("Executed DbCommand", StringComparison.Ordinal),
            "o EF Core precisa estar no log (nível Verbose) para o teste provar que ele não grava valores de parâmetros");
        all.Should().Contain(
            line => line.Contains("\"@l\":\"Debug\"", StringComparison.Ordinal) || line.Contains("\"@l\":\"Verbose\"", StringComparison.Ordinal));

        foreach (var (name, secret) in secrets)
        {
            all.Should().NotContain(
                line => line.Contains(secret, StringComparison.Ordinal),
                "{0} nunca pode aparecer no log, em nenhum nível", name);
        }

        // O header Authorization e o cookie também não: nem o esquema com o token, nem o par nome=valor.
        all.Should().NotContain(line => line.Contains("Bearer ey", StringComparison.Ordinal));
        all.Should().NotContain(line => line.Contains("refreshToken=", StringComparison.Ordinal));
    }

    [Fact] // CA-02, CA-03, CA-07b, CA-07c
    public async Task RequisicaoAutenticadaEAnonima_TraceIdCoincideEntreServicosEProblemDetails_UserIdSoQuandoAutenticado()
    {
        var email = $"correlacao-{Guid.NewGuid():N}@example.com";
        using var client = _chain.CreateGatewayClient();
        await Post(client, "/api/auth/register", new RegisterHttpRequest(email, Password, "Correlação"));

        // Anônima, com erro: o traceId do ProblemDetails é o do log de requisição do Gateway e o do log do Identity.
        var failed = await Post(client, "/api/auth/login", new LoginHttpRequest(email, WrongPassword));
        failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problemTraceId = (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("traceId").GetString()!;

        var gatewayRequest = await EntryAsync(_chain.GatewayLog, problemTraceId, "/api/auth/login");
        var identityLogin = await EntryAsync(_chain.IdentityLog, problemTraceId, "/identity.v1.IdentityService/Login");
        gatewayRequest.GetProperty("service").GetString().Should().Be("gateway");
        identityLogin.GetProperty("service").GetString().Should().Be("identity");
        gatewayRequest.GetProperty("StatusCode").GetInt32().Should().Be(401);
        gatewayRequest.TryGetProperty("userId", out _).Should().BeFalse("requisição anônima não leva o campo userId (nem vazio)");

        // Autenticada: o log de requisição do Gateway tem userId (o sub do token).
        var login = await Login(client, email, Password);
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        var meTraceId = Guid.NewGuid().ToString("N");
        me.Headers.Add("traceparent", $"00-{meTraceId}-0123456789abcdef-01");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        (await client.SendAsync(me)).StatusCode.Should().Be(HttpStatusCode.OK);

        var meEntry = await EntryAsync(_chain.GatewayLog, meTraceId, "/api/me");
        var userId = meEntry.GetProperty("userId").GetString();
        Guid.TryParse(userId, out _).Should().BeTrue();

        // E o Identity viu o mesmo traceId daquela requisição (CA-07c): o Gateway só repassa x-user-id ao Tasks,
        // então a entrada do Identity não tem userId (o id vai no corpo do RPC, nunca no log).
        var identityProfile = await EntryAsync(_chain.IdentityLog, meTraceId, "/identity.v1.IdentityService/GetProfile");
        identityProfile.GetProperty("service").GetString().Should().Be("identity");
    }

    [Fact] // CA-06, CA-02
    public async Task ExcecaoNaoTratada_Log500ComStackTraceNoLog_RespostaGenericaSemStack()
    {
        var email = $"excecao-{Guid.NewGuid():N}@example.com";
        using var client = _chain.CreateGatewayClient();
        await Post(client, "/api/auth/register", new RegisterHttpRequest(email, Password, "Exceção"));
        var login = await Login(client, email, Password);

        var path = $"/api/tasks/{Guid.NewGuid()}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContain(ThrowingTasksBackend.SecretDetail).And.NotContain("InvalidOperationException").And.NotContain("   at ");
        var traceId = JsonDocument.Parse(rawBody).RootElement.GetProperty("traceId").GetString()!;

        // O mesmo traceId do ProblemDetails está no log de requisição (500) e na entrada Error com a exceção e o stack.
        var requestLog = await EntryAsync(_chain.GatewayLog, traceId, path);
        requestLog.GetProperty("StatusCode").GetInt32().Should().Be(500);

        var error = _chain.GatewayLog.Lines
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(entry => entry.TryGetProperty("@l", out var level) && level.GetString() == "Error"
                && entry.GetProperty("traceId").GetString() == traceId
                && entry.GetProperty("SourceContext").GetString()!.EndsWith("GlobalExceptionHandler", StringComparison.Ordinal));
        var exception = error.GetProperty("@x").GetString()!;
        exception.Should().Contain("InvalidOperationException").And.Contain(ThrowingTasksBackend.SecretDetail).And.Contain("   at ");
    }

    /// <summary>
    /// A entrada do log de requisição daquele <c>traceId</c> — exatamente uma. O Serilog loga depois que a resposta
    /// já foi enviada, então o teste espera (até 5 s) a entrada aparecer em vez de ler o sink às cegas.
    /// </summary>
    private static async Task<JsonElement> EntryAsync(JsonLogSink sink, string traceId, string requestPath)
    {
        List<JsonElement> matches = [];

        for (var attempt = 0; attempt < 100 && matches.Count == 0; attempt++)
        {
            matches = sink.Lines
                .Select(line => JsonDocument.Parse(line).RootElement)
                .Where(entry => IsRequestLog(entry, requestPath) && entry.GetProperty("traceId").GetString() == traceId)
                .ToList();

            if (matches.Count == 0)
            {
                await Task.Delay(50);
            }
        }

        return matches.Should().ContainSingle($"deve haver exatamente uma entrada de {requestPath} com traceId {traceId}").Subject;
    }

    /// <summary>Só a entrada do log de requisição do Serilog (o ASP.NET Core também loga "Request starting" com o mesmo caminho).</summary>
    private static bool IsRequestLog(JsonElement entry, string requestPath) =>
        entry.TryGetProperty("RequestPath", out var path)
        && path.GetString() == requestPath
        && entry.TryGetProperty("SourceContext", out var source)
        && source.GetString() == "Serilog.AspNetCore.RequestLoggingMiddleware";

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(new Uri(url, UriKind.Relative), body);

    private static async Task<(string AccessToken, string RefreshToken)> Login(HttpClient client, string email, string password)
    {
        var response = await Post(client, "/api/auth/login", new LoginHttpRequest(email, password));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadSession(response);
    }

    private static async Task<(string AccessToken, string RefreshToken)> Refresh(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={refreshToken}");
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadSession(response);
    }

    private static async Task<(string AccessToken, string RefreshToken)> ReadSession(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("refreshToken=", StringComparison.Ordinal));
        var refreshToken = cookie["refreshToken=".Length..].Split(';')[0];

        return (body.GetProperty("accessToken").GetString()!, refreshToken);
    }
}
