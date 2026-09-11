using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Infrastructure.Users;
using Xunit;

namespace TodoList.Identity.IntegrationTests;

/// <summary>
/// Servidor gRPC real do Identity, subido via <see cref="WebApplicationFactory{TEntryPoint}"/> —
/// BE-26 CA-01 (aceita conexões gRPC), CA-03 (um cliente de teste invoca
/// <c>ValidateUser</c> de verdade, não só compila) e CA-08 (id malformado não
/// derruba o servidor). CA-13 (reflexo em tempo real do banco) não é testável
/// nesta etapa: não há persistência de usuário ligada (BE-04/BE-07 pendentes),
/// só o seed em memória.
/// </summary>
public class ValidateUserGrpcTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ValidateUserGrpcTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact] // CA-01, CA-03, CA-05
    public async Task ValidateUser_UsuarioAtivoDoSeed_RetornaExistsEActiveTrue()
    {
        using var client = CreateClient();

        var response = await client.ValidateUserAsync(new ValidateUserRequest { UserId = InMemoryUserLookup.ActiveUserId.ToString() });

        response.Exists.Should().BeTrue();
        response.Active.Should().BeTrue();
        response.DisplayName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-06
    public async Task ValidateUser_UsuarioInativoDoSeed_RetornaActiveFalse()
    {
        using var client = CreateClient();

        var response = await client.ValidateUserAsync(new ValidateUserRequest { UserId = InMemoryUserLookup.InactiveUserId.ToString() });

        response.Exists.Should().BeTrue();
        response.Active.Should().BeFalse();
    }

    [Fact] // CA-07
    public async Task ValidateUser_UsuarioInexistente_RetornaExistsFalse()
    {
        using var client = CreateClient();

        var response = await client.ValidateUserAsync(new ValidateUserRequest { UserId = Guid.NewGuid().ToString() });

        response.Exists.Should().BeFalse();
        response.Active.Should().BeFalse();
        response.DisplayName.Should().Be(string.Empty);
    }

    [Fact] // CA-08 — id malformado não derruba o servidor: status OK, resposta negativa.
    public async Task ValidateUser_UserIdMalformado_RetornaStatusOkComRespostaNegativa()
    {
        using var client = CreateClient();
        var call = client.ValidateUserAsync(new ValidateUserRequest { UserId = "isto-nao-e-um-guid" });

        var response = await call;
        var status = call.GetStatus();

        status.StatusCode.Should().Be(StatusCode.OK);
        response.Exists.Should().BeFalse();
        response.Active.Should().BeFalse();
        response.DisplayName.Should().Be(string.Empty);
    }

    [Fact] // BE-34, CA-03 — token malformado (não-JWT) responde valid=false mesmo por rede real.
    public async Task ValidateToken_TokenMalformado_RetornaValidFalse()
    {
        using var client = CreateClient();

        var response = await client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = "qualquer-coisa" });

        response.Valid.Should().BeFalse();
        response.UserId.Should().Be(string.Empty);
    }

    [Fact] // BE-33, CA-11 — UserStore:Provider=InMemory (padrão desta factory) nega qualquer login.
    public async Task Login_ProviderInMemory_RetornaSucceededFalse()
    {
        using var client = CreateClient();

        var response = await client.LoginAsync(new LoginRequest { Email = "ada.lovelace@todolist.example", Password = "qualquer-senha" });

        response.Succeeded.Should().BeFalse();
        response.AccessToken.Should().BeEmpty();
    }

    private IdentityGrpcTestClient CreateClient()
    {
        var handler = _factory.Server.CreateHandler();
        return new IdentityGrpcTestClient(handler, _factory.Server.BaseAddress);
    }
}
