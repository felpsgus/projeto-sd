using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-40, D-38 — <c>POST /api/tasks</c> como rota protegida representativa:
/// CA-08 a CA-15 (<c>AddJwtBearer</c> local, sem perguntar ao Identity). Cada
/// causa de rejeição (HS256, <c>alg=none</c>, outra chave, expirado, iss/aud
/// errados, ausente) devolve exatamente o mesmo 401 — nenhuma é
/// distinguível pelo cliente (CA-15).
/// </summary>
public class AuthenticationTests : IClassFixture<GatewayApiFactory>
{
    private const string Subject = "11111111-1111-1111-1111-111111111111";

    private static readonly CreateTaskHttpRequest _payloadValido = new("Título", null, null, null);
    private static readonly CreateTaskHttpRequest _payloadInvalido = new(null, null, null, null);

    private readonly GatewayApiFactory _factory;

    public AuthenticationTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-08
    public async Task CreateTask_TokenHs256_Retorna401()
    {
        var token = JwtTestTokens.CreateHs256(Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-09
    public async Task CreateTask_TokenAlgNoneSemAssinatura_Retorna401()
    {
        var token = JwtTestTokens.CreateNoneAlgorithm(Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-10
    public async Task CreateTask_TokenAssinadoPorOutraChaveRsa_Retorna401()
    {
        var token = JwtTestTokens.CreateSignedByOtherKey(Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-11 — ClockSkew zero: 1 segundo depois do exp já é o suficiente
    public async Task CreateTask_TokenExpirado_Retorna401MesmoUmSegundoDepoisDoExp()
    {
        var token = JwtTestTokens.CreateExpired(_factory.SigningKey, Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-12
    public async Task CreateTask_TokenComIssuerErrado_Retorna401()
    {
        var token = JwtTestTokens.CreateWithWrongIssuer(_factory.SigningKey, Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-12
    public async Task CreateTask_TokenComAudienceErrada_Retorna401()
    {
        var token = JwtTestTokens.CreateWithWrongAudience(_factory.SigningKey, Subject);

        var response = await PostWithTokenAsync(token, _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-08 CA-10 / BE-13 CA-04 — payload adulterado, assinatura original
    public async Task CreateTask_PayloadAdulteradoComAssinaturaOriginal_Retorna401()
    {
        var partes = JwtTestTokens.CreateValid(_factory.SigningKey, Subject).Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(partes[1]));
        var adulterado = Base64UrlEncoder.Encode(payload.Replace(Subject, "22222222-2222-2222-2222-222222222222", StringComparison.Ordinal));
        adulterado.Should().NotBe(partes[1]);

        var response = await PostWithTokenAsync($"{partes[0]}.{adulterado}.{partes[2]}", _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory] // BE-13 CA-04 — lixo no Bearer é 401, nunca 500
    [InlineData("isto-nao-e-um-jwt")]
    [InlineData("")]
    public async Task CreateTask_BearerMalformadoOuVazio_Retorna401(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-09 (numeração de BE-36: ausência de token) — sem Authorization
    public async Task CreateTask_SemAuthorizationHeader_Retorna401MesmoComPayloadValido()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // autenticação vence validação de payload — ordem observável
    public async Task CreateTask_SemTokenEComPayloadInvalido_Retorna401NaoQuatrocentos()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadInvalido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-15 + regressão traceId — todas as causas de 401 devolvem o mesmo corpo,
           // exceto o traceId (por requisição), que precisa vir preenchido em todas
    public async Task CreateTask_TodasAsCausasDe401_DevolvemOMesmoCorpoComTraceIdProprio()
    {
        var semToken = await _factory.CreateClient().PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);
        var semTokenBody = await semToken.Content.ReadAsStringAsync();
        var semTokenBodySemTraceId = ExtractTraceIdAndNormalize(semTokenBody, out var semTokenTraceId);

        semTokenTraceId.Should().NotBeNullOrEmpty();

        var causas = new[]
        {
            JwtTestTokens.CreateHs256(Subject),
            JwtTestTokens.CreateNoneAlgorithm(Subject),
            JwtTestTokens.CreateSignedByOtherKey(Subject),
            JwtTestTokens.CreateExpired(_factory.SigningKey, Subject),
            JwtTestTokens.CreateWithWrongIssuer(_factory.SigningKey, Subject),
            JwtTestTokens.CreateWithWrongAudience(_factory.SigningKey, Subject),
        };

        foreach (var token in causas)
        {
            var response = await PostWithTokenAsync(token, _payloadValido);
            var body = await response.Content.ReadAsStringAsync();
            var bodySemTraceId = ExtractTraceIdAndNormalize(body, out var traceId);

            response.StatusCode.Should().Be(semToken.StatusCode);
            traceId.Should().NotBeNullOrEmpty();
            bodySemTraceId.Should().Be(semTokenBodySemTraceId);
        }
    }

    /// <summary>
    /// Remove o campo <c>traceId</c> (único por requisição, via
    /// <c>CustomizeProblemDetails</c>) e devolve o corpo normalizado, para
    /// comparar o restante do 401 entre causas diferentes (CA-15) sem exigir
    /// igualdade literal do JSON inteiro.
    /// </summary>
    private static string ExtractTraceIdAndNormalize(string body, out string? traceId)
    {
        var node = JsonNode.Parse(body)!.AsObject();
        traceId = node["traceId"]?.GetValue<string>();
        node.Remove("traceId");

        return node.ToJsonString();
    }

    private async Task<HttpResponseMessage> PostWithTokenAsync(string token, CreateTaskHttpRequest payload)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), payload);
    }
}
