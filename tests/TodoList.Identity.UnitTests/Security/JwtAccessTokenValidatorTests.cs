using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtAccessTokenValidator"/> (BE-08) — CA-01, CA-07 a CA-10, e
/// entradas malformadas (nulo/vazio/lixo) nunca lançam exceção. O CA-07 usa
/// <see cref="FakeTimeProvider"/> tanto para emitir quanto para validar —
/// nunca um token cujo <c>exp</c> já esteja no passado real (nota técnica de
/// BE-08/BE-34: a armadilha de tempo do <c>JsonWebTokenHandler</c>).
/// </summary>
public class JwtAccessTokenValidatorTests
{
    private const string SigningKey = "01234567890123456789012345678901"; // 32 bytes
    private const string OutraChave = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"; // 32 bytes, diferente
    private const string Issuer = "todolist-identity";
    private const string Audience = "todolist";

    private static readonly Email _email = Email.Create("ada@exemplo.com").Value;

    [Fact] // CA-01
    public async Task ValidateAsync_ComTokenGeradoPeloProprioServico_RetornaValidoComUserId()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator) = CreatePair(timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.UserId.Should().Be(user.Id);
    }

    [Fact] // CA-07 — 1 segundo antes do exp, aceito
    public async Task ValidateAsync_UmSegundoAntesDoExp_Aceita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator) = CreatePair(timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);

        timeProvider.SetUtcNow(accessToken.ExpiresAt - TimeSpan.FromSeconds(1));
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeTrue();
    }

    [Fact] // CA-07 — 1 segundo depois do exp, rejeitado (ClockSkew zero)
    public async Task ValidateAsync_UmSegundoAposOExp_Rejeita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator) = CreatePair(timeProvider);

        var accessToken = tokenService.GenerateAccessToken(user);

        timeProvider.SetUtcNow(accessToken.ExpiresAt + TimeSpan.FromSeconds(1));
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.UserId.Should().BeNull();
    }

    [Fact] // CA-08 — assinado com outra chave
    public async Task ValidateAsync_ComTokenAssinadoComOutraChave_Rejeita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var tokenServiceComOutraChave = CreateTokenService(timeProvider, signingKey: OutraChave);
        var validator = CreateValidator(timeProvider, signingKey: SigningKey);

        var accessToken = tokenServiceComOutraChave.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-09 — issuer diferente
    public async Task ValidateAsync_ComIssuerDiferente_Rejeita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var tokenServiceComOutroIssuer = CreateTokenService(timeProvider, issuer: "outro-issuer");
        var validator = CreateValidator(timeProvider, issuer: Issuer);

        var accessToken = tokenServiceComOutroIssuer.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-09 — audience diferente
    public async Task ValidateAsync_ComAudienceDiferente_Rejeita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);

        var tokenServiceComOutraAudience = CreateTokenService(timeProvider, audience: "outra-audience");
        var validator = CreateValidator(timeProvider, audience: Audience);

        var accessToken = tokenServiceComOutraAudience.GenerateAccessToken(user);
        var result = await validator.ValidateAsync(accessToken.Token, CancellationToken.None);

        result.IsValid.Should().BeFalse();
    }

    [Fact] // CA-10 — payload alterado quebra a assinatura
    public async Task ValidateAsync_ComPayloadAlterado_Rejeita()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (tokenService, validator) = CreatePair(timeProvider);

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
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var validator = CreateValidator(timeProvider);

        var act = async () => await validator.ValidateAsync(tokenMalformado, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.IsValid.Should().BeFalse();
    }

    private static User CreateUser(FakeTimeProvider timeProvider) =>
        User.Create(_email, "Ada Lovelace", "hash-qualquer", timeProvider).Value;

    private static (JwtTokenService TokenService, JwtAccessTokenValidator Validator) CreatePair(FakeTimeProvider timeProvider) =>
        (CreateTokenService(timeProvider), CreateValidator(timeProvider));

    private static JwtTokenService CreateTokenService(
        FakeTimeProvider timeProvider,
        string signingKey = SigningKey,
        string issuer = Issuer,
        string audience = Audience,
        int accessTokenMinutes = JwtOptions.DefaultAccessTokenMinutes) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = signingKey,
            AccessTokenMinutes = accessTokenMinutes,
        }), timeProvider);

    private static JwtAccessTokenValidator CreateValidator(
        FakeTimeProvider timeProvider,
        string signingKey = SigningKey,
        string issuer = Issuer,
        string audience = Audience)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = signingKey,
        });

        var validationParameters = new JwtValidationParameters(options, timeProvider);

        return new JwtAccessTokenValidator(validationParameters, NullLogger<JwtAccessTokenValidator>.Instance);
    }
}
