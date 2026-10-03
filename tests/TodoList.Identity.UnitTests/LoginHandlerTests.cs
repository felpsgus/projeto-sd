using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using TodoList.Identity.UnitTests.Authentication;
using TodoList.Identity.UnitTests.Security;
using TodoList.Identity.UnitTests.Sessions;
using TodoList.SharedKernel;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="LoginHandler"/> (BE-33) com <see cref="IUserRepository"/>
/// substituído, <see cref="Pbkdf2PasswordHasher"/> real (custo reduzido — mesma
/// exigência de BE-06) e <see cref="JwtTokenService"/> real com chave de teste
/// e <see cref="FakeTimeProvider"/>. CA-02 a CA-05: as quatro/cinco causas de
/// falha produzem exatamente o mesmo <see cref="TodoList.SharedKernel.Error"/>,
/// e o tempo de resposta não denuncia qual delas ocorreu.
/// </summary>
public class LoginHandlerTests : IDisposable
{
    private const string RegisteredEmail = "ada@exemplo.com";
    private const string CorrectPassword = "senha-correta-123";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly TestRsaKeyFile _jwtKeyFile = TestRsaKeyFile.Create();
    private readonly InMemoryRefreshTokenRepository _refreshRepository = new();
    private readonly InMemoryLoginAttemptStore _attempts = new();
    private readonly List<(string Plain, string Hash)> _verifyCalls = [];
    private readonly DummyPasswordHash _dummyPasswordHash;
    private LockoutOptions _lockout = new();
    private readonly User _activeUser;
    private readonly User _inactiveUser;

    public LoginHandlerTests()
    {
        _dummyPasswordHash = new DummyPasswordHash(_passwordHasher);
        _activeUser = User.Create(
            Email.Create(RegisteredEmail).Value, "Ada Lovelace", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;

        _inactiveUser = User.Create(
            Email.Create("charles@exemplo.com").Value, "Charles Babbage", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;
        _inactiveUser.Deactivate(_timeProvider);
    }

    [Fact] // sucesso — controle, para contraste com os cenários de falha abaixo
    public async Task HandleAsync_CredenciaisCorretasUsuarioAtivo_RetornaSucessoComAccessToken()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be(_activeUser.Id);
        result.Value.AccessToken.Token.Should().NotBeNullOrEmpty();
    }

    [Fact] // BE-09 CA-03 — e-mail em qualquer combinação de maiúsculas/minúsculas
    public async Task HandleAsync_EmailComOutraCaixa_Autentica()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync("  ADA@Exemplo.COM ", CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // BE-09 CA-10 / BE-10 — cada login abre uma sessão nova, com refresh token próprio
    public async Task HandleAsync_DoisLogins_TemSessoesERefreshTokensDiferentes()
    {
        var sut = CreateHandler(_activeUser);

        var first = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);
        var second = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        first.Value.RefreshToken.Value.Should().NotBe(second.Value.RefreshToken.Value);
        first.Value.RefreshToken.SessionId.Should().NotBe(second.Value.RefreshToken.SessionId);
    }

    [Fact] // BE-09 CA-10b (camada de aplicação): falha não emite refresh token — não há o que emitir
    public async Task HandleAsync_Falha_NaoPersisteRefreshToken()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        _refreshRepository.Tokens.Should().BeEmpty();
    }

    [Fact] // CA-02
    public async Task HandleAsync_SenhaErrada_RetornaInvalidCredentials()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCredentials);
    }

    [Fact] // CA-03
    public async Task HandleAsync_EmailInexistente_RetornaInvalidCredentials()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCredentials);
    }

    [Fact] // CA-04
    public async Task HandleAsync_UsuarioInativoComSenhaCorreta_RetornaInvalidCredentials()
    {
        var sut = CreateHandler(_inactiveUser);

        var result = await sut.HandleAsync(_inactiveUser.Email.Value, CorrectPassword, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCredentials);
    }

    [Fact] // e-mail malformado — mesmo caminho único de falha
    public async Task HandleAsync_EmailMalformado_RetornaInvalidCredentials()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync("isto-nao-e-um-email", CorrectPassword, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCredentials);
    }

    [Fact] // senha vazia — mesmo caminho único de falha
    public async Task HandleAsync_SenhaVazia_RetornaInvalidCredentials()
    {
        var sut = CreateHandler(_activeUser);

        var result = await sut.HandleAsync(RegisteredEmail, string.Empty, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCredentials);
    }

    [Fact] // CA-02 a CA-04 — todas as causas de falha produzem exatamente o mesmo Error
    public async Task HandleAsync_TodasAsCausasDeFalha_ProduzemOMesmoError()
    {
        var sut = CreateHandler(_activeUser, _inactiveUser);

        var senhaErrada = await sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None);
        var emailInexistente = await sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None);
        var usuarioInativo = await sut.HandleAsync(_inactiveUser.Email.Value, CorrectPassword, CancellationToken.None);
        var emailMalformado = await sut.HandleAsync("nao-e-email", CorrectPassword, CancellationToken.None);
        var senhaVazia = await sut.HandleAsync(RegisteredEmail, string.Empty, CancellationToken.None);

        var erros = new[] { senhaErrada.Error, emailInexistente.Error, usuarioInativo.Error, emailMalformado.Error, senhaVazia.Error };
        erros.Should().AllBeEquivalentTo(AuthErrors.InvalidCredentials);
    }

    [Fact] // CA-05 — e-mail inexistente paga o mesmo Verify (hash dummy) que senha errada: sem canal de tempo
    public async Task HandleAsync_EmailInexistenteESenhaErrada_PagamOMesmoCustoDeVerify()
    {
        // Determinístico: em vez de comparar tempos de parede (frágil sob carga), afirma que o
        // verificador de hash FOI chamado em ambos os caminhos, exatamente uma vez cada.
        var sut = CreateHandler(_activeUser);

        await sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None);
        var chamadasSenhaErrada = _verifyCalls.ToArray();
        _verifyCalls.Clear();
        await sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None);
        var chamadasEmailInexistente = _verifyCalls.ToArray();

        chamadasSenhaErrada.Should().ContainSingle().Which.Hash.Should().Be(_activeUser.PasswordHash);
        chamadasEmailInexistente.Should().ContainSingle()
            .Which.Hash.Should().Be(_dummyPasswordHash.Value, "e-mail inexistente verifica contra o hash dummy (CA-05 de BE-33)");
    }

    // ---- BE-12: bloqueio temporário por tentativas (relógio fake, nunca dorme) ----

    private static async Task FailAsync(LoginHandler sut, int times, string email = RegisteredEmail)
    {
        for (var i = 0; i < times; i++)
        {
            (await sut.HandleAsync(email, "senha-errada", CancellationToken.None)).Error.Should().Be(AuthErrors.InvalidCredentials);
        }
    }

    [Fact] // BE-12 CA-01
    public async Task HandleAsync_QuintaFalha_AindaEProcessadaComoCredencialInvalida()
    {
        var sut = CreateHandler(_activeUser);

        await FailAsync(sut, 5);
    }

    [Fact] // BE-12 CA-02 — mesmo com a senha correta, e sem verificar a senha
    public async Task HandleAsync_SextaTentativaAposCincoFalhas_BloqueiaMesmoComSenhaCorreta()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 5);

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(AuthErrors.TooManyAttemptsCode);
        result.Error.Type.Should().Be(ErrorType.TooManyRequests);
        result.Error.RetryAfter.Should().Be(TimeSpan.FromMinutes(15));
        _refreshRepository.Tokens.Should().BeEmpty();
    }

    [Fact] // BE-12 CA-03 (tempo restante diminui com o relógio)
    public async Task HandleAsync_Bloqueado_RetryAfterDiminuiComORelogio()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 5);
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.Error.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact] // BE-12 CA-04
    public async Task HandleAsync_PassadoOBloqueio_LoginComSenhaCorretaSucede()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 5);
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // BE-12 CA-05
    public async Task HandleAsync_SucessoZeraContador_SaoNecessariasCincoNovasFalhas()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 4);
        (await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None)).IsSuccess.Should().BeTrue();

        await FailAsync(sut, 5);

        (await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None)).Error.Code.Should().Be(AuthErrors.TooManyAttemptsCode);
    }

    [Fact] // BE-12 CA-06
    public async Task HandleAsync_BloqueioEPorEmail_OutroEmailNaoEAfetado()
    {
        var other = User.Create(Email.Create("grace@exemplo.com").Value, "Grace", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;
        var sut = CreateHandler(_activeUser, other);
        await FailAsync(sut, 5);

        var result = await sut.HandleAsync("grace@exemplo.com", CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // BE-12 CA-07
    public async Task HandleAsync_CaixaDiferenteSomaNoMesmoContador()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 3, "ADA@exemplo.com");
        await FailAsync(sut, 2, " ada@Exemplo.com ");

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.Error.Code.Should().Be(AuthErrors.TooManyAttemptsCode);
    }

    [Fact] // BE-12 CA-08 / ADR 0002 — e-mail inexistente bloqueia igual
    public async Task HandleAsync_EmailInexistenteComCincoTentativas_Bloqueia()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 5, "ninguem@exemplo.com");

        var result = await sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None);

        result.Error.Code.Should().Be(AuthErrors.TooManyAttemptsCode);
    }

    [Fact] // ADR 0002 — e-mail malformado não gera linha no contador
    public async Task HandleAsync_EmailMalformado_NaoContaTentativa()
    {
        var sut = CreateHandler(_activeUser);

        await FailAsync(sut, 8, "nao-e-email");

        _attempts.RowCount.Should().Be(0);
    }

    [Fact] // BE-12 CA-09
    public async Task HandleAsync_TentativasEspacadasAlemDaJanela_NaoAcumulam()
    {
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 4);
        _timeProvider.Advance(TimeSpan.FromMinutes(20));
        await FailAsync(sut, 2);

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact] // BE-12 CA-12
    public async Task HandleAsync_LockoutDesabilitado_NuncaBloqueia()
    {
        _lockout = new LockoutOptions { Enabled = false };
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 10);

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _attempts.RowCount.Should().Be(0);
    }

    [Fact] // BE-12 CA-13 — só configuração muda
    public async Task HandleAsync_MaxAttemptsTres_BloqueiaNaQuartaTentativa()
    {
        _lockout = new LockoutOptions { MaxAttempts = 3 };
        var sut = CreateHandler(_activeUser);
        await FailAsync(sut, 3);

        var result = await sut.HandleAsync(RegisteredEmail, CorrectPassword, CancellationToken.None);

        result.Error.Code.Should().Be(AuthErrors.TooManyAttemptsCode);
    }

    public void Dispose()
    {
        _jwtKeyFile.Dispose();
        GC.SuppressFinalize(this);
    }

    private LoginHandler CreateHandler(params User[] users)
    {
        var repository = Substitute.For<IUserRepository>();

        foreach (var user in users)
        {
            repository.GetByEmailAsync(Arg.Is<Email>(e => e.Value == user.Email.Value), Arg.Any<CancellationToken>()).Returns(user);
        }

        repository
            .GetByEmailAsync(Arg.Is<Email>(e => users.All(u => u.Email.Value != e.Value)), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var jwtOptions = Options.Create(new JwtOptions { Issuer = "todolist-identity", Audience = "todolist", PrivateKeyPath = _jwtKeyFile.Path });
        var signingKeyProvider = new RsaSigningKeyProvider(jwtOptions);
        var tokenService = new JwtTokenService(jwtOptions, _timeProvider, signingKeyProvider);

        var refreshTokens = new RefreshTokenService(_refreshRepository, Substitute.For<IUnitOfWork>(), _timeProvider, TimeSpan.FromDays(7));

        return new LoginHandler(repository, new RecordingHasher(_passwordHasher, _verifyCalls), tokenService, _dummyPasswordHash, refreshTokens, _attempts, _lockout, _timeProvider);
    }

    private sealed class RecordingHasher(IPasswordHasher inner, List<(string Plain, string Hash)> calls) : IPasswordHasher
    {
        public string Hash(string plainPassword) => inner.Hash(plainPassword);

        public bool Verify(string plainPassword, string hash)
        {
            calls.Add((plainPassword, hash));

            return inner.Verify(plainPassword, hash);
        }
    }
}
