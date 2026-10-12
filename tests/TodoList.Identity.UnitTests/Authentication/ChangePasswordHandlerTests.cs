using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using TodoList.SharedKernel;
using Xunit;

namespace TodoList.Identity.UnitTests.Authentication;

/// <summary>
/// <see cref="ChangePasswordHandler"/> (BE-15) — CA-04, CA-05, CA-06 e a
/// revogação de sessões da troca de senha (RN-AUTH-19, BE-15 CA-07 a CA-09).
/// </summary>
public class ChangePasswordHandlerTests
{
    private const string CurrentPassword = "senha-atual-123";
    private const string NewPassword = "senha-nova-456";

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly User _user;

    public ChangePasswordHandlerTests()
    {
        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", _passwordHasher.Hash(CurrentPassword), _timeProvider).Value;
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
    }

    [Fact] // sucesso — controle
    public async Task HandleAsync_SenhaAtualCorretaENovaValida_AlteraHashEPersiste()
    {
        var sut = CreateHandler();
        var hashAntes = _user.PasswordHash;

        var result = await sut.HandleAsync(new ChangePasswordRequest(_user.Id, CurrentPassword, NewPassword), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _user.PasswordHash.Should().NotBe(hashAntes);
        _passwordHasher.Verify(NewPassword, _user.PasswordHash).Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // RN-AUTH-19: a troca revoga todas as sessões, antes do mesmo SaveChanges que grava o hash
    public async Task HandleAsync_Sucesso_RevogaTodasAsSessoesAntesDeSalvar()
    {
        var sut = CreateHandler();

        await sut.HandleAsync(new ChangePasswordRequest(_user.Id, CurrentPassword, NewPassword), CancellationToken.None);

        Received.InOrder(() =>
        {
            _refreshTokenRepository.RevokeAllForUserAsync(_user.Id, RefreshTokenRevocationReason.PasswordChanged, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Theory] // senha atual errada / nova fora da política / nova igual: nenhuma sessão é revogada
    [InlineData("senha-errada", NewPassword)]
    [InlineData(CurrentPassword, "curta")]
    [InlineData(CurrentPassword, CurrentPassword)]
    public async Task HandleAsync_Falha_NaoRevogaSessoes(string currentPassword, string newPassword)
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new ChangePasswordRequest(_user.Id, currentPassword, newPassword), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _refreshTokenRepository.DidNotReceive().RevokeAllForUserAsync(
            Arg.Any<Guid>(), Arg.Any<RefreshTokenRevocationReason>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact] // CA-04: senha atual incorreta não altera o hash
    public async Task HandleAsync_SenhaAtualIncorreta_RetornaErroENaoAlteraHash()
    {
        var sut = CreateHandler();
        var hashAntes = _user.PasswordHash;

        var result = await sut.HandleAsync(new ChangePasswordRequest(_user.Id, "senha-errada", NewPassword), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.InvalidCurrentPassword);
        _user.PasswordHash.Should().Be(hashAntes);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact] // CA-05: nova senha fora da política
    public async Task HandleAsync_NovaSenhaForaDaPolitica_RetornaErroDeValidacao()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new ChangePasswordRequest(_user.Id, CurrentPassword, "curta"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(TodoList.SharedKernel.ErrorType.Validation);
    }

    [Fact] // CA-06: nova senha igual à atual
    public async Task HandleAsync_NovaSenhaIgualAtual_RetornaErro()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new ChangePasswordRequest(_user.Id, CurrentPassword, CurrentPassword), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.NewPasswordSameAsCurrent);
    }

    [Fact] // BE-16/BE-15: usuário não encontrado (sessão de conta já excluída)
    public async Task HandleAsync_UsuarioNaoEncontrado_RetornaUserNotFound()
    {
        var sut = CreateHandler();

        var result = await sut.HandleAsync(new ChangePasswordRequest(Guid.NewGuid(), CurrentPassword, NewPassword), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.UserNotFound);
    }

    private ChangePasswordHandler CreateHandler() =>
        new(_userRepository, _unitOfWork, _passwordHasher, _timeProvider, new RefreshTokenService(_refreshTokenRepository, _unitOfWork, _timeProvider, TimeSpan.FromDays(7)));
}
