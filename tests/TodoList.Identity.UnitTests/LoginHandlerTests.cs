using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
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
public class LoginHandlerTests
{
    private const string RegisteredEmail = "ada@exemplo.com";
    private const string CorrectPassword = "senha-correta-123";

    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 200 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly User _activeUser;
    private readonly User _inactiveUser;

    public LoginHandlerTests()
    {
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

    [Fact] // CA-05 — tempo de "e-mail inexistente" e "senha errada" na mesma ordem de grandeza (hash dummy pago)
    public async Task HandleAsync_TempoDeEmailInexistenteESenhaErrada_MesmaOrdemDeGrandeza()
    {
        const int rounds = 8;
        var sut = CreateHandler(_activeUser);

        // Aquecimento — primeira chamada de cada tipo tende a ser mais lenta
        // (JIT/alocações), o que poluiria a média.
        await sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None);
        await sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None);

        var duracaoSenhaErrada = await MedirDuracaoMediaAsync(rounds, () => sut.HandleAsync(RegisteredEmail, "senha-errada", CancellationToken.None));
        var duracaoEmailInexistente = await MedirDuracaoMediaAsync(rounds, () => sut.HandleAsync("ninguem@exemplo.com", CorrectPassword, CancellationToken.None));

        var maior = Math.Max(duracaoSenhaErrada, duracaoEmailInexistente);
        var menor = Math.Max(Math.Min(duracaoSenhaErrada, duracaoEmailInexistente), 0.0001);

        (maior / menor).Should().BeLessThan(5, "e-mail inexistente também paga o custo do hash dummy (CA-05 de BE-33)");
    }

    private static async Task<double> MedirDuracaoMediaAsync(int rounds, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < rounds; i++)
        {
            await action();
        }

        return stopwatch.Elapsed.TotalMilliseconds / rounds;
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

        var tokenService = new JwtTokenService(
            Options.Create(new JwtOptions { Issuer = "todolist-identity", Audience = "todolist", SigningKey = "01234567890123456789012345678901" }),
            _timeProvider);
        var dummyPasswordHash = new DummyPasswordHash(_passwordHasher);

        return new LoginHandler(repository, _passwordHasher, tokenService, dummyPasswordHash);
    }
}
