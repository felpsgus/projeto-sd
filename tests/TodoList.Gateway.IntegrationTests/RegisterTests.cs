using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>BE-07 — <c>POST /api/auth/register</c>: CA-01, CA-05, CA-08, CA-13.</summary>
public class RegisterTests : IClassFixture<GatewayApiFactory>
{
    private readonly GatewayApiFactory _factory;

    public RegisterTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // CA-01: sucesso é 201 com id/email/displayName/createdAt
    public async Task Register_DadosValidos_Retorna201ComPerfil()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid().ToString();
        _factory.Identity.RegisterHandler = (email, _, displayName) => (userId, email, displayName, createdAt);

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative), new RegisterHttpRequest("nova@example.com", "senha123", "Nova Conta"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ProfileHttpResponse>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(userId);
        body.Email.Should().Be("nova@example.com");
        body.DisplayName.Should().Be("Nova Conta");
    }

    [Fact] // CA-05: e-mail duplicado é 409 com o error-code do catálogo (D-35)
    public async Task Register_EmailDuplicado_Retorna409()
    {
        _factory.Identity.RegisterHandler = (_, _, _) =>
            throw new RpcException(
                new Status(StatusCode.FailedPrecondition, "Este e-mail já está cadastrado."),
                new Metadata { { "error-code", "auth.email_already_registered" } });

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative), new RegisterHttpRequest("duplicado@example.com", "senha123", null));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("auth.email_already_registered");
    }

    [Fact] // CA-08: senha fora da política é 400, sem chamar o Identity
    public async Task Register_SenhaForaDaPolitica_Retorna400SemChamarOIdentity()
    {
        var callCountAntes = _factory.Identity.RegisterCallCount;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative), new RegisterHttpRequest("nova@example.com", "curta", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Identity.RegisterCallCount.Should().Be(callCountAntes);
    }

    [Fact] // CA-13: acessível sem token
    public async Task Register_SemToken_NaoRetorna401()
    {
        _factory.Identity.RegisterHandler = (email, _, displayName) => (Guid.NewGuid().ToString(), email, displayName, DateTimeOffset.UtcNow);

        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/register", UriKind.Relative), new RegisterHttpRequest("anonimo@example.com", "senha123", null));

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
