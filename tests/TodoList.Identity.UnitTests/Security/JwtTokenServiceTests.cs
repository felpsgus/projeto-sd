using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtTokenService"/> (BE-08) — CA-04 (claims sub/email), CA-05
/// (nenhum dado sensível), CA-06 (duração configurável) e CA-11 (jti
/// distinto). Usa <see cref="JsonWebTokenHandler.ReadJsonWebToken"/> só para
/// <b>decodificar</b> o payload nos testes (leitura, sem validar assinatura)
/// — é a mesma biblioteca usada para emitir o token.
/// </summary>
public class JwtTokenServiceTests
{
    private static readonly Email _email = Email.Create("ada@exemplo.com").Value;
    private const string SigningKey = "01234567890123456789012345678901"; // 32 bytes

    [Fact] // CA-04
    public void GenerateAccessToken_ContemSubIgualAoIdEEmailNormalizado()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        jwt.Claims.Single(c => c.Type == "sub").Value.Should().Be(user.Id.ToString());
        jwt.Claims.Single(c => c.Type == "email").Value.Should().Be(user.Email.Value);
    }

    [Fact] // CA-05 — o conjunto exato de claims exigido, nada mais
    public void GenerateAccessToken_ContemExatamenteOConjuntoDeClaimsExigido()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        var tiposDeClaimPresentes = jwt.Claims.Select(c => c.Type).ToHashSet();

        tiposDeClaimPresentes.Should().BeEquivalentTo(["sub", "email", "jti", "iat", "exp", "iss", "aud"]);
    }

    [Fact] // CA-06 — padrão de 15 minutos
    public void GenerateAccessToken_ComDuracaoPadrao_ExpEmQuinzeMinutosAposIat()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(JwtOptions.DefaultAccessTokenMinutes));
        accessToken.ExpiresAt.Should().Be(timeProvider.GetUtcNow().AddMinutes(JwtOptions.DefaultAccessTokenMinutes));
    }

    [Fact] // CA-06 — valor alternativo configurado
    public void GenerateAccessToken_ComDuracaoConfiguradaDiferente_RespeitaOValorConfigurado()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider, accessTokenMinutes: 45);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(45));
    }

    [Fact] // CA-11
    public void GenerateAccessToken_ChamadoDuasVezes_ProduzJtiDistintos()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider);

        var token1 = service.GenerateAccessToken(user);
        var token2 = service.GenerateAccessToken(user);

        var jti1 = new JsonWebTokenHandler().ReadJsonWebToken(token1.Token).Claims.Single(c => c.Type == "jti").Value;
        var jti2 = new JsonWebTokenHandler().ReadJsonWebToken(token2.Token).Claims.Single(c => c.Type == "jti").Value;

        jti1.Should().NotBe(jti2);
    }

    [Fact] // CA-13 — AccessToken.ToString() não expõe o token
    public void AccessToken_ToString_NaoContemOToken()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var service = CreateService(timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        accessToken.ToString().Should().NotContain(accessToken.Token);
    }

    private static User CreateUser(FakeTimeProvider timeProvider) =>
        User.Create(_email, "Ada Lovelace", "hash-qualquer", timeProvider).Value;

    private static JwtTokenService CreateService(FakeTimeProvider timeProvider, int accessTokenMinutes = JwtOptions.DefaultAccessTokenMinutes)
    {
        var options = new JwtOptions
        {
            Issuer = "todolist-identity",
            Audience = "todolist",
            SigningKey = SigningKey,
            AccessTokenMinutes = accessTokenMinutes,
        };

        return new JwtTokenService(Options.Create(options), timeProvider);
    }
}
