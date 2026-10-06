using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Security;

/// <summary>
/// <see cref="JwtTokenService"/> (BE-08, RS256 desde BE-40/D-38) — CA-01
/// (<c>alg=RS256</c> e <c>kid</c> presente), CA-02 (<c>kid</c> estável e
/// igual ao thumbprint RFC 7638 da chave pública), CA-04 (claims sub/email),
/// CA-05 (nenhum dado sensível), CA-06 (duração configurável) e CA-11 (jti
/// distinto). Usa <see cref="JsonWebTokenHandler.ReadJsonWebToken"/> só para
/// <b>decodificar</b> o payload/header nos testes (leitura, sem validar
/// assinatura) — é a mesma biblioteca usada para emitir o token. A chave RSA
/// é gerada em memória (<see cref="TestRsaKeyFile"/>), nunca versionada.
/// </summary>
public class JwtTokenServiceTests
{
    private static readonly Email _email = Email.Create("ada@exemplo.com").Value;

    [Fact] // CA-01
    public void GenerateAccessToken_TemAlgRS256EKidPresente()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        jwt.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
        jwt.Kid.Should().NotBeNullOrWhiteSpace();
    }

    [Fact] // CA-02 — kid estável entre chamadas e igual ao thumbprint da chave pública
    public void GenerateAccessToken_ChamadoDuasVezes_KidEstavelEIgualAoThumbprintDaChavePublica()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, signingKeyProvider) = CreateService(chave.Path, timeProvider);

        var token1 = service.GenerateAccessToken(user);
        var token2 = service.GenerateAccessToken(user);

        var kid1 = new JsonWebTokenHandler().ReadJsonWebToken(token1.Token).Kid;
        var kid2 = new JsonWebTokenHandler().ReadJsonWebToken(token2.Token).Kid;

        kid1.Should().Be(kid2);
        kid1.Should().Be(signingKeyProvider.KeyId);
    }

    [Fact] // CA-04
    public void GenerateAccessToken_ContemSubIgualAoIdEEmailNormalizado()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        jwt.Claims.Single(c => c.Type == "sub").Value.Should().Be(user.Id.ToString());
        jwt.Claims.Single(c => c.Type == "email").Value.Should().Be(user.Email.Value);
    }

    [Fact] // CA-05 — o conjunto exato de claims exigido, nada mais
    public void GenerateAccessToken_ContemExatamenteOConjuntoDeClaimsExigido()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        var tiposDeClaimPresentes = jwt.Claims.Select(c => c.Type).ToHashSet();

        tiposDeClaimPresentes.Should().BeEquivalentTo(["sub", "email", "jti", "iat", "exp", "iss", "aud"]);
    }

    [Fact] // CA-06 — padrão de 15 minutos
    public void GenerateAccessToken_ComDuracaoPadrao_ExpEmQuinzeMinutosAposIat()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(JwtOptions.DefaultAccessTokenMinutes));
        accessToken.ExpiresAt.Should().Be(timeProvider.GetUtcNow().AddMinutes(JwtOptions.DefaultAccessTokenMinutes));
    }

    [Fact] // CA-06 — valor alternativo configurado
    public void GenerateAccessToken_ComDuracaoConfiguradaDiferente_RespeitaOValorConfigurado()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider, accessTokenMinutes: 45);

        var accessToken = service.GenerateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken.Token);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(45));
    }

    [Fact] // CA-11
    public void GenerateAccessToken_ChamadoDuasVezes_ProduzJtiDistintos()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var token1 = service.GenerateAccessToken(user);
        var token2 = service.GenerateAccessToken(user);

        var jti1 = new JsonWebTokenHandler().ReadJsonWebToken(token1.Token).Claims.Single(c => c.Type == "jti").Value;
        var jti2 = new JsonWebTokenHandler().ReadJsonWebToken(token2.Token).Claims.Single(c => c.Type == "jti").Value;

        jti1.Should().NotBe(jti2);
    }

    [Fact] // CA-13 — AccessToken.ToString() não expõe o token
    public void AccessToken_ToString_NaoContemOToken()
    {
        using var chave = TestRsaKeyFile.Create();
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
        var user = CreateUser(timeProvider);
        var (service, _) = CreateService(chave.Path, timeProvider);

        var accessToken = service.GenerateAccessToken(user);

        accessToken.ToString().Should().NotContain(accessToken.Token);
    }

    private static User CreateUser(FakeTimeProvider timeProvider) =>
        User.Create(_email, "Ada Lovelace", "hash-qualquer", timeProvider).Value;

    private static (JwtTokenService Service, RsaSigningKeyProvider SigningKeyProvider) CreateService(
        string privateKeyPath, FakeTimeProvider timeProvider, int accessTokenMinutes = JwtOptions.DefaultAccessTokenMinutes)
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "todolist-identity",
            Audience = "todolist",
            PrivateKeyPath = privateKeyPath,
            AccessTokenMinutes = accessTokenMinutes,
        });

        var signingKeyProvider = new RsaSigningKeyProvider(options);

        return (new JwtTokenService(options, timeProvider, signingKeyProvider), signingKeyProvider);
    }
}
