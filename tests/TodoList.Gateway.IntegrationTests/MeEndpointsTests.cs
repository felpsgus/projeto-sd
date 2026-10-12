using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using TodoList.Gateway.Api.Contracts;
using Xunit;

namespace TodoList.Gateway.IntegrationTests;

/// <summary>
/// BE-14/BE-15/BE-16 — <c>GET/PATCH /api/me</c>, <c>POST /api/me/change-password</c>
/// e <c>DELETE /api/me</c>: autenticação, tradução de sucesso e o caminho de
/// erro da decisão do tech lead (senha atual incorreta é 400, não 401).
/// </summary>
public class MeEndpointsTests : IClassFixture<GatewayApiFactory>
{
    private const string AuthenticatedUserId = "33333333-3333-3333-3333-333333333333";

    private readonly GatewayApiFactory _factory;

    public MeEndpointsTests(GatewayApiFactory factory)
    {
        _factory = factory;
    }

    [Fact] // BE-14, CA-01
    public async Task GetMe_TokenValido_Retorna200ComPerfilDoUsuarioDoToken()
    {
        var createdAt = DateTimeOffset.UtcNow;
        _factory.Identity.GetProfileHandler = userId => (userId, "ada@example.com", "Ada", createdAt);

        var response = await AuthenticatedClient().GetAsync(new Uri("/api/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProfileHttpResponse>();
        body!.Id.Should().Be(AuthenticatedUserId);
        body.Email.Should().Be("ada@example.com");
    }

    [Fact] // BE-14, CA-03
    public async Task GetMe_SemToken_Retorna401()
    {
        var response = await _factory.CreateClient().GetAsync(new Uri("/api/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-14 — o user_id enviado ao Identity é o sub do token, nunca um valor do cliente
    public async Task GetMe_EnviaAoIdentityOUserIdDoToken()
    {
        _factory.Identity.GetProfileHandler = userId => (userId, "ada@example.com", "Ada", DateTimeOffset.UtcNow);

        await AuthenticatedClient().GetAsync(new Uri("/api/me", UriKind.Relative));

        _factory.Identity.LastGetProfileUserId.Should().Be(AuthenticatedUserId);
    }

    [Fact] // BE-14, CA-05
    public async Task PatchMe_DisplayNameValido_Retorna200ComPerfilAtualizado()
    {
        _factory.Identity.UpdateProfileHandler = (userId, displayName) => (userId, "ada@example.com", displayName, DateTimeOffset.UtcNow);

        var response = await AuthenticatedClient().PatchAsJsonAsync(
            new Uri("/api/me", UriKind.Relative), new UpdateProfileHttpRequest("Novo Nome"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ProfileHttpResponse>();
        body!.DisplayName.Should().Be("Novo Nome");
    }

    [Fact] // BE-14, CA-06
    public async Task PatchMe_DisplayNameVazio_Retorna400SemChamarOIdentity()
    {
        var callCountAntes = _factory.Identity.UpdateProfileCallCount;

        var response = await AuthenticatedClient().PatchAsJsonAsync(
            new Uri("/api/me", UriKind.Relative), new UpdateProfileHttpRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Identity.UpdateProfileCallCount.Should().Be(callCountAntes);
    }

    [Fact] // BE-14, CA-12
    public async Task PatchMe_SemToken_Retorna401()
    {
        var response = await _factory.CreateClient().PatchAsJsonAsync(
            new Uri("/api/me", UriKind.Relative), new UpdateProfileHttpRequest("Nome"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-15, CA-01/CA-15: sucesso é 204 sem corpo
    public async Task ChangePassword_DadosValidos_Retorna204()
    {
        _factory.Identity.ChangePasswordHandler = (_, _, _) => { };

        var response = await AuthenticatedClient().PostAsJsonAsync(
            new Uri("/api/me/change-password", UriKind.Relative),
            new ChangePasswordHttpRequest("senha-atual-123", "senha-nova-456"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact] // Decisão do tech lead: senha atual incorreta é 400 com erro no campo currentPassword, nunca 401
    public async Task ChangePassword_SenhaAtualIncorreta_Retorna400ComErroNoCampoCurrentPassword()
    {
        _factory.Identity.ChangePasswordHandler = (_, _, _) =>
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "A senha atual informada está incorreta."),
                new Metadata
                {
                    { "error-code", "auth.invalid_current_password" },
                    { "validation-errors", "{\"CurrentPassword\":[\"A senha atual informada está incorreta.\"]}" },
                });

        var response = await AuthenticatedClient().PostAsJsonAsync(
            new Uri("/api/me/change-password", UriKind.Relative),
            new ChangePasswordHttpRequest("senha-errada", "senha-nova-456"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("auth.invalid_current_password");
        body.Should().Contain("currentPassword", "a chave do dicionário errors deve estar em camelCase");
    }

    [Fact] // BE-15, CA-13
    public async Task ChangePassword_SemToken_Retorna401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            new Uri("/api/me/change-password", UriKind.Relative),
            new ChangePasswordHttpRequest("senha-atual-123", "senha-nova-456"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-15 — o user_id enviado ao Identity é o sub do token
    public async Task ChangePassword_EnviaAoIdentityOUserIdDoToken()
    {
        _factory.Identity.ChangePasswordHandler = (_, _, _) => { };

        await AuthenticatedClient().PostAsJsonAsync(
            new Uri("/api/me/change-password", UriKind.Relative),
            new ChangePasswordHttpRequest("senha-atual-123", "senha-nova-456"));

        _factory.Identity.LastChangePasswordUserId.Should().Be(AuthenticatedUserId);
    }

    [Fact] // BE-16, CA-01: sucesso é 204
    public async Task DeleteMe_SenhaCorreta_Retorna204()
    {
        _factory.Identity.DeleteAccountHandler = (_, _) => { };

        var response = await SendDeleteWithBodyAsync(new DeleteAccountHttpRequest("senha-correta-123"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact] // Decisão do tech lead: senha incorreta é 400 com erro no campo password, nunca 401
    public async Task DeleteMe_SenhaIncorreta_Retorna400ComErroNoCampoPassword()
    {
        _factory.Identity.DeleteAccountHandler = (_, _) =>
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, "A senha atual informada está incorreta."),
                new Metadata
                {
                    { "error-code", "auth.invalid_current_password" },
                    { "validation-errors", "{\"Password\":[\"A senha atual informada está incorreta.\"]}" },
                });

        var response = await SendDeleteWithBodyAsync(new DeleteAccountHttpRequest("senha-errada"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("auth.invalid_current_password");
        body.Should().Contain("\"password\"");
    }

    [Fact] // BE-16, CA-12
    public async Task DeleteMe_SemToken_Retorna401()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/me", UriKind.Relative))
        {
            Content = JsonContent.Create(new DeleteAccountHttpRequest("senha-correta-123")),
        };

        var response = await _factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] // BE-16 — o user_id enviado ao Identity é o sub do token
    public async Task DeleteMe_EnviaAoIdentityOUserIdDoToken()
    {
        _factory.Identity.DeleteAccountHandler = (_, _) => { };

        await SendDeleteWithBodyAsync(new DeleteAccountHttpRequest("senha-correta-123"));

        _factory.Identity.LastDeleteAccountUserId.Should().Be(AuthenticatedUserId);
    }

    private async Task<HttpResponseMessage> SendDeleteWithBodyAsync(DeleteAccountHttpRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/me", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };

        return await AuthenticatedClient().SendAsync(request);
    }

    private HttpClient AuthenticatedClient()
    {
        var token = JwtTestTokens.CreateValid(_factory.SigningKey, AuthenticatedUserId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }
}
