using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtAccessTokenValidator"/> (BE-08, RS256 desde BE-40/D-38) —
/// CA-01, CA-07 a CA-10 (agora com chave RSA), e as novas rejeições exigidas
/// por D-38: HS256, <c>alg=none</c> e RS256 assinado por outra chave privada
/// nunca validam — sempre <c>valid=false</c>, nunca exceção. Entradas
/// malformadas (nulo/vazio/lixo) também nunca lançam. Todas as chaves são
/// geradas em memória (<see cref="TestRsaKeyFile"/>), nenhuma versionada.
/// </summary>
public class JwtAccessTokenValidatorTests
{
    private const string Issuer = "todolist-identity";
    private const string Audience = "todolist";

    private static readonly Email _email = Email.Create("ada@exemplo.com").Value;

    [Fact] // CA-01
    public async Task ValidateAsync_ComTokenGeradoPeloProprioServico_RetornaValidoComUserId()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator, _) = CreatePair(chave.Path, timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UserId.Should().Be(user.Id);
    }

    [Fact] // CA-07 — 1 segundo antes do exp, aceito
    public async Task ValidateAsync_UmSegundoAntesDoExp_Aceita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator, _) = CreatePair(chave.Path, timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);

        timeProvider.SetUtcNow(accessToken.ExpiresAt - TimeSpan.FromSeconds(1));
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-07 — 1 segundo depois do exp, rejeitado (ClockSkew zero)
    public async Task ValidateAsync_UmSegundoAposOExp_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator, _) = CreatePair(chave.Path, timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);

        timeProvider.SetUtcNow(accessToken.ExpiresAt + TimeSpan.FromSeconds(1));
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.UserId.Should().BeNull();
    }

    [Fact] // CA-08/CA-10 (D-38) — RS256 assinado por OUTRA chave privada RSA
    public async Task ValidateAsync_ComTokenAssinadoComOutraChaveRsa_Rejeita()
    {
        using var chaveEmitida = TestRsaKeyFile.Create();
        using var chaveOutra = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var (tokenServiceComOutraChave, _, _) = CreatePair(chaveOutra.Path, timeProvider);
        var (_, validator, _) = CreatePair(chaveEmitida.Path, timeProvider);

        var accessToken = tokenServiceComOutraChave.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // D-38 — token HS256 (mesmo com uma chave "parecida") é rejeitado
    public async Task ValidateAsync_ComTokenHS256_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var (_, validator, _) = CreatePair(chave.Path, timeProvider);

        var hs256Token = CreateHs256Token(timeProvider);
        var result = await validator.ValidateAsync(hs256Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // D-38 — token com alg=none e sem assinatura é rejeitado
    public async Task ValidateAsync_ComTokenAlgNone_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var (_, validator, _) = CreatePair(chave.Path, timeProvider);

        var noneToken = CreateUnsignedToken(timeProvider);
        var result = await validator.ValidateAsync(noneToken, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-09 — issuer diferente
    public async Task ValidateAsync_ComIssuerDiferente_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var (tokenServiceComOutroIssuer, _, _) = CreatePair(chave.Path, timeProvider, issuer: "outro-issuer");
        var (_, validator, _) = CreatePair(chave.Path, timeProvider, issuer: Issuer);

        var accessToken = tokenServiceComOutroIssuer.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-09 — audience diferente
    public async Task ValidateAsync_ComAudienceDiferente_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var (tokenServiceComOutraAudience, _, _) = CreatePair(chave.Path, timeProvider, audience: "outra-audience");
        var (_, validator, _) = CreatePair(chave.Path, timeProvider, audience: Audience);

        var accessToken = tokenServiceComOutraAudience.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-10 — payload alterado quebra a assinatura
    public async Task ValidateAsync_ComPayloadAlterado_Rejeita()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator, _) = CreatePair(chave.Path, timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);
        var partes = accessToken.Token.Split('.');
        var payloadAdulterado = partes[1].Length > 0
            ? (partes[1][0] == 'A' ? 'B' : 'A') + partes[1][1..]
            : partes[1];
        var tokenAdulterado = $"{partes[0]}.{payloadAdulterado}.{partes[2]}";

        var result = await validator.ValidateAsync(tokenAdulterado, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Theory] // token nulo/vazio/lixo nunca lança, sempre Invalid
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("isto-nao-e-um-jwt")]
    [InlineData("a.b.c")]
    public async Task ValidateAsync_ComTokenMalformado_RetornaInvalidoSemLancar(string? tokenMalformado)
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var (_, validator, _) = CreatePair(chave.Path, timeProvider);

        var act = async () => await validator.ValidateAsync(tokenMalformado, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.IsValid.Should().BeFalse();
    }

    private static User CreateUser(FakeTimeProvider timeProvider) =>
        User.Create(_email, "Ada Lovelace", "hash-qualquer", timeProvider).Value;

    private static (JwtTokenService TokenService, JwtAccessTokenValidator Validator, RsaSigningKeyProvider SigningKeyProvider) CreatePair(
        string privateKeyPath,
        FakeTimeProvider timeProvider,
        string issuer = Issuer,
        string audience = Audience)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            PrivateKeyPath = privateKeyPath,
        });

        var signingKeyProvider = new RsaSigningKeyProvider(options);
        var tokenService = new JwtTokenService(options, timeProvider, signingKeyProvider);
        var validationParameters = new JwtValidationParameters(options, timeProvider, signingKeyProvider);
        var validator = new JwtAccessTokenValidator(validationParameters, NullLogger<JwtAccessTokenValidator>.Instance);

        return (tokenService, validator, signingKeyProvider);
    }

    /// <summary>Token HS256 forjado à mão, fora de <see cref="JwtTokenService"/> (que só emite RS256) — usado para provar a rejeição exigida por D-38.</summary>
    private static string CreateHs256Token(FakeTimeProvider timeProvider)
    {
        var chaveSimetrica = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("uma-chave-simetrica-de-32-bytes!"));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = timeProvider.GetUtcNow().UtcDateTime,
            Expires = timeProvider.GetUtcNow().AddMinutes(15).UtcDateTime,
            SigningCredentials = new SigningCredentials(chaveSimetrica, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString() },
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    /// <summary>Token sem assinatura (<c>alg=none</c>) — usado para provar a rejeição exigida por D-38.</summary>
    private static string CreateUnsignedToken(FakeTimeProvider timeProvider)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = timeProvider.GetUtcNow().UtcDateTime,
            Expires = timeProvider.GetUtcNow().AddMinutes(15).UtcDateTime,
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = Guid.NewGuid().ToString() },
        };

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}
