using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-36 — <c>POST /api/tasks</c> como rota protegida representativa: CA-09
/// a CA-13 (autenticação vence validação; corpo 401 idêntico; indisponibilidade
/// do Identity nunca vira 401).
/// </summary>
public class AuthenticationTests : IClassFixture<GatewayApiFactory>
{
    private static readonly CreateTaskHttpRequest _payloadValido = new("Título", null, null, null);
    private static readonly CreateTaskHttpRequest _payloadInvalido = new(null, null, null, null);

    private readonly GatewayApiFactory _factory;

    public AuthenticationTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-09
    public async Task CreateTask_SemAuthorizationHeader_Retorna401MesmoComPayloadValido()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-10
    public async Task CreateTask_TokenInvalidoOuExpirado_Retorna401()
    {
        _factory.Identity.ValidateTokenHandler = _ => (false, string.Empty);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-11 — ordem autenticação-antes-de-validação é observável
    public async Task CreateTask_SemTokenEComPayloadInvalido_Retorna401NaoQuatrocentos()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadInvalido);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // CA-12 — mesmo corpo 401 para token ausente e token inválido
    public async Task CreateTask_TokenAusenteETokenInvalido_DevolvemOMesmoCorpo401()
    {
        var semTokenClient = _factory.CreateClient();
        var semTokenResponse = await semTokenClient.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);
        var semTokenBody = await semTokenResponse.Content.ReadAsStringAsync();

        _factory.Identity.ValidateTokenHandler = _ => (false, string.Empty);
        var tokenInvalidoClient = _factory.CreateClient();
        tokenInvalidoClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        var tokenInvalidoResponse = await tokenInvalidoClient.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);
        var tokenInvalidoBody = await tokenInvalidoResponse.Content.ReadAsStringAsync();

        semTokenResponse.StatusCode.Should().Be(tokenInvalidoResponse.StatusCode);
        semTokenBody.Should().Be(tokenInvalidoBody);
    }

    [Fact] // CA-13 — Identity inalcançável durante ValidateToken nunca vira 401
    public async Task CreateTask_IdentityIndisponivelDuranteValidateToken_Retorna503ComRetryAfterNunca401()
    {
        _factory.Identity.ValidateTokenHandler = _ =>
            throw new RpcException(new Status(StatusCode.Unavailable, "Identity fora do ar."));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-qualquer");

        var response = await client.PostAsJsonAsync(new Uri("/api/tasks", UriKind.Relative), _payloadValido);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
    }
}
