using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-36 — <c>POST /api/auth/login</c>: CA-02, CA-03, CA-24.</summary>
public class AuthLoginTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public AuthLoginTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-02
    public async Task Login_CredenciaisValidas_Retorna200ComAccessTokenEExpiresAt()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        _factory.Identity.LoginHandler = (_, _) => (true, "token-valido", expiresAt, Guid.NewGuid().ToString());

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginHttpResponse>();
        body.Should().NotBeNull();
        body!.AccessToken.Should().Be("token-valido");
    }

    [Theory] // CA-03: e-mail inexistente, senha errada e usuário inativo são indistinguíveis
    [InlineData("inexistente@example.com", "qualquer")]
    [InlineData("user@example.com", "senha-errada")]
    [InlineData("inativo@example.com", "senha123")]
    public async Task Login_CredenciaisInvalidas_Retorna401ComMesmoCorpoParaTodasAsCausas(string email, string password)
    {
        _factory.Identity.LoginHandler = (_, _) => (false, string.Empty, default, string.Empty);

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest(email, password));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("auth.invalid_credentials");
        body.Should().NotContain(email, "o corpo não deve variar por causa/credencial");
    }

    [Fact] // BE-12 CA-03: 429 + Retry-After em segundos + errorCode, sem cookie, no-store
    public async Task Login_Bloqueado_Retorna429ComRetryAfterSemCookie()
    {
        _factory.Identity.LoginLockedOutRetryAfterSeconds = 840;

        try
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsJsonAsync(
                new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));

            response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(840));
            response.Headers.Contains("Set-Cookie").Should().BeFalse();
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            (await response.Content.ReadAsStringAsync()).Should().Contain("auth.too_many_attempts");
        }
        finally
        {
            _factory.Identity.LoginLockedOutRetryAfterSeconds = null;
        }
    }

    [Fact] // CA-24
    public async Task Login_IdentityIndisponivel_Retorna503ComRetryAfterNunca401()
    {
        _factory.Identity.LoginHandler = (_, _) =>
            throw new RpcException(new Status(StatusCode.Unavailable, "Identity fora do ar."));

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative), new LoginHttpRequest("user@example.com", "senha123"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter.Should().NotBeNull();
    }
}
