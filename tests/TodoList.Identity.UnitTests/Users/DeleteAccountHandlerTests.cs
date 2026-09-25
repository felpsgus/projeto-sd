using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;

namespace TodoList.Identity.UnitTests.Users;

/// <summary>
/// <see cref="DeleteAccountHandler"/> (BE-16) — CA-10 (senha incorreta não
/// apaga nada). O restante dos critérios (CA-01 a CA-09, CA-11 a CA-13) exige
/// Postgres real (cascata de FK, atomicidade de transação) e está coberto por
/// teste de integração.
/// </summary>
public class DeleteAccountHandlerTests
{
    private const string CorrectPassword = "senha-correta-123";

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly User _user;

    public DeleteAccountHandlerTests()
    {
        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    [Fact] // sucesso — controle
    public async Task HandleAsync_SenhaCorreta_RemoveUsuarioEPersiste()
    {
        var sut = new DeleteAccountHandler(_userRepository, _unitOfWork, _passwordHasher);

        var result = await sut.HandleAsync(_user.Id, CorrectPassword, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRepository.Received(1).Remove(_user);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-10: senha incorreta — nada é apagado
    public async Task HandleAsync_SenhaIncorreta_NaoRemoveNadaERetornaErro()
    {
        var sut = new DeleteAccountHandler(_userRepository, _unitOfWork, _passwordHasher);

        var result = await sut.HandleAsync(_user.Id, "senha-errada", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCurrentPassword);
        _userRepository.DidNotReceive().Remove(Arg.Any<User>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // usuário inexistente (chamada redundante) — não lança
    public async Task HandleAsync_UsuarioInexistente_RetornaUserNotFound()
    {
        var sut = new DeleteAccountHandler(_userRepository, _unitOfWork, _passwordHasher);

        var result = await sut.HandleAsync(Guid.NewGuid(), CorrectPassword, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.UserNotFound);
    }
}
