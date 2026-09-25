using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Authentication;

/// <summary>
/// <see cref="RegisterUserHandler"/> (BE-07) com <see cref="IUserRepository"/>/
/// <see cref="IUnitOfWork"/> substituídos e <see cref="Pbkdf2PasswordHasher"/>
/// real (custo reduzido, mesma exigência de BE-06) — CA-02, CA-05, CA-09 a
/// CA-12.
/// </summary>
public class RegisterUserHandlerTests
{
    private const string NewEmail = "nova@exemplo.com";
    private const string ValidPassword = "senha123";

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));

    [Fact] // CA-02: usuário criado tem IsActive == true e PasswordHash preenchido e diferente da senha
    public async Task HandleAsync_DadosValidos_CriaUsuarioAtivoComHashDiferenteDaSenha()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, "Ada"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be(NewEmail);
        _userRepository.Received(1).Add(Arg.Is<User>(u => u.IsActive && u.PasswordHash != ValidPassword));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-05: e-mail já existente retorna Conflict
    public async Task HandleAsync_EmailJaExistente_RetornaConflict()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, "Ada"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.EmailAlreadyRegistered);
        _userRepository.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact] // CA-06: e-mail em caixa diferente de um já existente também é Conflict — a checagem opera sobre Email já normalizado
    public async Task HandleAsync_EmailComCaixaDiferenteDeExistente_RetornaConflict()
    {
        _userRepository
            .EmailExistsAsync(Arg.Is<Email>(e => e.Value == "joao@exemplo.com"), Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest("JOAO@Exemplo.com", ValidPassword, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.EmailAlreadyRegistered);
    }

    [Fact] // CA-07: formato de e-mail inválido
    public async Task HandleAsync_EmailFormatoInvalido_RetornaErroDeValidacao()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest("nao-e-email", ValidPassword, null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(UserErrors.EmailInvalidFormat);
    }

    [Fact] // CA-08: senha fora da política
    public async Task HandleAsync_SenhaForaDaPolitica_RetornaErroDeSenha()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, "curta1", null), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(TodoList.SharedKernel.ErrorType.Validation);
    }

    [Fact] // CA-09: sem displayName, usa a parte antes do "@"
    public async Task HandleAsync_SemDisplayName_UsaParteAntesDoArroba()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, null), CancellationToken.None);

        result.Value.DisplayName.Should().Be("nova");
    }

    [Fact] // CA-10: displayName só espaços recebe o mesmo tratamento de ausente
    public async Task HandleAsync_DisplayNameSoEspacos_UsaParteAntesDoArroba()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, "   "), CancellationToken.None);

        result.Value.DisplayName.Should().Be("nova");
    }

    [Fact] // CA-11: displayName informado é preservado
    public async Task HandleAsync_DisplayNameInformado_PreservaValor()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, "Ada Lovelace"), CancellationToken.None);

        result.Value.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact] // CA-12: violação de unicidade sob concorrência vira Conflict, não exceção não tratada
    public async Task HandleAsync_SaveChangesLancaViolacaoDeUnicidade_RetornaConflictSemPropagarExcecao()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new UniqueConstraintViolationException());
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new RegisterUserRequest(NewEmail, ValidPassword, "Ada"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.EmailAlreadyRegistered);
    }

    private RegisterUserHandler CreateHandler() =>
        new(_userRepository, _unitOfWork, _passwordHasher, _timeProvider);
}
