using System.Buffers.Text;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using Xunit;

namespace TodoList.Identity.UnitTests.Sessions;

/// <summary>
/// <see cref="RefreshTokenService"/> (BE-10, BE-11) sobre o repositório em memória:
/// as cinco falhas, rotação, reuso derrubando a cadeia, sessões independentes.
/// A atomicidade real no banco está em <c>RefreshTokenPostgresTests</c> (Docker).
/// </summary>
public class RefreshTokenServiceTests
{
    private static readonly Guid _userId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid _otherUserId = Guid.Parse("40000000-0000-0000-0000-000000000002");

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T10:00:00Z"));
    private readonly RefreshTokenService _sut;
    private readonly InMemoryRefreshTokenRepository _repository;

    public RefreshTokenServiceTests()
    {
        (_sut, _repository) = InMemoryRefreshTokenRepository.CreateService(_time);
    }

    [Fact] // CA-19
    public void Generate_ValoresDistintosCom32BytesOuMaisEmBase64Url()
    {
        var first = RefreshTokenSecret.Generate();
        var second = RefreshTokenSecret.Generate();

        first.Should().NotBe(second);
        Base64Url.DecodeFromChars(first).Length.Should().BeGreaterThanOrEqualTo(32);
        first.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact] // CA-17, CA-07
    public async Task Issue_GuardaSoOHashEExpiraEmSetePorPadrao()
    {
        var issued = await _sut.IssueAsync(_userId, sessionId: null, CancellationToken.None);

        var stored = _repository.Tokens.Should().ContainSingle().Subject;
        stored.TokenHash.Should().Be(RefreshTokenSecret.Hash(issued.Value)).And.NotBe(issued.Value);
        stored.TokenHash.Should().HaveLength(64);
        stored.SessionId.Should().Be(issued.SessionId);
        issued.ExpiresAt.Should().Be(_time.GetUtcNow().AddDays(7));
    }

    [Fact] // CA-10 / D-15: cada login é uma sessão
    public async Task Issue_DoisLoginsDoMesmoUsuario_TemSessoesETokensDiferentesEAmbosValidos()
    {
        var first = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var second = await _sut.IssueAsync(_userId, null, CancellationToken.None);

        first.Value.Should().NotBe(second.Value);
        first.SessionId.Should().NotBe(second.SessionId);
        (await _sut.RedeemAsync(first.Value, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _sut.RedeemAsync(second.Value, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact] // CA-03, CA-06, CA-08: rotação mantém a sessão, troca o valor e encadeia
    public async Task Redeem_Valido_RotacionaNaMesmaSessaoEEncadeia()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var current = login;

        for (var i = 0; i < 3; i++)
        {
            var result = await _sut.RedeemAsync(current.Value, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.UserId.Should().Be(_userId);
            result.Value.Next.Value.Should().NotBe(current.Value);
            result.Value.Next.SessionId.Should().Be(login.SessionId);
            current = result.Value.Next;
        }

        var chain = _repository.Tokens;
        chain.Should().HaveCount(4).And.OnlyContain(token => token.SessionId == login.SessionId);
        chain.Count(token => token.ConsumedAt is null).Should().Be(1, "só o último da cadeia continua utilizável");
        chain.Where(token => token.ConsumedAt is not null).Should().OnlyContain(token => token.ReplacedByTokenId != null);
    }

    [Theory] // CA-13..CA-16: nulo, vazio, só espaços
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Redeem_TokenAusente_Falha(string? token)
    {
        var result = await _sut.RedeemAsync(token, CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact] // CA-15
    public async Task Redeem_Inexistente_Falha()
    {
        var result = await _sut.RedeemAsync(RefreshTokenSecret.Generate(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact] // CA-13 / RN-AUTH-18: relógio avançado, sem Thread.Sleep
    public async Task Redeem_Expirado_Falha()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(7));

        var result = await _sut.RedeemAsync(login.Value, CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact] // CA-14
    public async Task Redeem_Revogado_Falha()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        await _sut.RevokeSessionAsync(login.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);

        var result = await _sut.RedeemAsync(login.Value, CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
    }

    [Fact] // CA-08, CA-09, CA-10 / RN-AUTH-17
    public async Task Redeem_TokenJaConsumido_FalhaERevogaACadeiaInteira()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var rotated = (await _sut.RedeemAsync(login.Value, CancellationToken.None)).Value.Next;

        var reuse = await _sut.RedeemAsync(login.Value, CancellationToken.None);

        reuse.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        (await _sut.RedeemAsync(rotated.Value, CancellationToken.None)).IsFailure
            .Should().BeTrue("o token legítimo mais recente também cai (RN-AUTH-17)");
        _repository.Tokens.Should().OnlyContain(token => token.RevokedAt != null)
            .And.OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact] // CA-11 / D-15
    public async Task Redeem_ReusoNaSessaoA_NaoAfetaSessaoB()
    {
        var sessionA = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var sessionB = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        await _sut.RedeemAsync(sessionA.Value, CancellationToken.None);
        await _sut.RedeemAsync(sessionA.Value, CancellationToken.None); // reuso

        var survivor = await _sut.RedeemAsync(sessionB.Value, CancellationToken.None);

        survivor.IsSuccess.Should().BeTrue();
    }

    [Fact] // CA-12 (lógica): quem perde a corrida do consumo trata como reuso e derruba tudo, inclusive o token do vencedor
    public async Task Redeem_PerdeACorridaDoConsumo_FalhaESessaoInteiraFicaRevogada()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var loginToken = _repository.Tokens.Single();
        var winnerToken = Guid.NewGuid();

        // Entre o "ler" e o "consumir" do perdedor, o vencedor já consumiu.
        _repository.BeforeConsume = () => loginToken.Consume(winnerToken, _time.GetUtcNow().UtcDateTime);

        var result = await _sut.RedeemAsync(login.Value, CancellationToken.None);

        result.Error.Should().Be(AuthErrors.InvalidRefreshToken);
        _repository.Tokens.Where(token => token.ConsumedAt is null).Should().OnlyContain(token => token.RevokedAt != null);
    }

    [Fact] // BE-11 CA-03, CA-04: revoga a cadeia inteira com motivo Logout
    public async Task RevokeSessionOfToken_TokenDoUsuario_RevogaACadeiaInteira()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var rotated = (await _sut.RedeemAsync(login.Value, CancellationToken.None)).Value.Next;

        var outcome = await _sut.RevokeSessionOfTokenAsync(_userId, rotated.Value, CancellationToken.None);

        outcome.Should().Be(RevokeSessionOutcome.Revoked);
        _repository.Tokens.Should().OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.Logout);
        (await _sut.RedeemAsync(rotated.Value, CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    [Fact] // BE-11 CA-05
    public async Task RevokeSessionOfToken_NaoDerrubaOutraSessaoDoMesmoUsuario()
    {
        var sessionA = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var sessionB = await _sut.IssueAsync(_userId, null, CancellationToken.None);

        await _sut.RevokeSessionOfTokenAsync(_userId, sessionA.Value, CancellationToken.None);

        (await _sut.RedeemAsync(sessionB.Value, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Theory] // BE-11 CA-08: idempotente — sem token, desconhecido
    [InlineData(null)]
    [InlineData("")]
    [InlineData("token-que-nunca-existiu")]
    public async Task RevokeSessionOfToken_AusenteOuDesconhecido_NaoFazNada(string? token)
    {
        var outcome = await _sut.RevokeSessionOfTokenAsync(_userId, token, CancellationToken.None);

        outcome.Should().Be(RevokeSessionOutcome.NotFound);
    }

    [Fact] // BE-11 CA-08: logout repetido continua sem erro
    public async Task RevokeSessionOfToken_JaRevogado_ContinuaSemErro()
    {
        var login = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        await _sut.RevokeSessionOfTokenAsync(_userId, login.Value, CancellationToken.None);

        var again = await _sut.RevokeSessionOfTokenAsync(_userId, login.Value, CancellationToken.None);

        again.Should().Be(RevokeSessionOutcome.Revoked);
    }

    [Fact] // BE-11 CA-09
    public async Task RevokeSessionOfToken_TokenDeOutroUsuario_NaoRevogaNada()
    {
        var victim = await _sut.IssueAsync(_otherUserId, null, CancellationToken.None);

        var outcome = await _sut.RevokeSessionOfTokenAsync(_userId, victim.Value, CancellationToken.None);

        outcome.Should().Be(RevokeSessionOutcome.OwnedByAnotherUser);
        _repository.Tokens.Should().OnlyContain(token => token.RevokedAt == null);
        (await _sut.RedeemAsync(victim.Value, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact] // BE-11 CA-06 / RN-AUTH-19
    public async Task RevokeAllForUser_RevogaTodasAsSessoesDoUsuarioESoDele()
    {
        var a = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var b = await _sut.IssueAsync(_userId, null, CancellationToken.None);
        var other = await _sut.IssueAsync(_otherUserId, null, CancellationToken.None);

        await _sut.RevokeAllForUserAsync(_userId, RefreshTokenRevocationReason.PasswordChanged, CancellationToken.None);

        (await _sut.RedeemAsync(a.Value, CancellationToken.None)).IsFailure.Should().BeTrue();
        (await _sut.RedeemAsync(b.Value, CancellationToken.None)).IsFailure.Should().BeTrue();
        (await _sut.RedeemAsync(other.Value, CancellationToken.None)).IsSuccess.Should().BeTrue();
        _repository.Tokens.Where(token => token.UserId == _userId)
            .Should().OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.PasswordChanged);
    }

    [Fact]
    public async Task IssuedRefreshToken_ToString_NuncaImprimeOValor()
    {
        var issued = await _sut.IssueAsync(_userId, null, CancellationToken.None);

        issued.ToString().Should().NotContain(issued.Value);
    }
}
