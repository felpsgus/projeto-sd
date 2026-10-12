using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using Xunit;

namespace TodoList.Identity.UnitTests.Users;

/// <summary><see cref="GetProfileHandler"/> (BE-14) — CA-01, CA-04.</summary>
public class GetProfileHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));

    [Fact] // CA-01
    public async Task HandleAsync_UsuarioExistente_RetornaPerfil()
    {
        var user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", "hash-qualquer", _timeProvider).Value;
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var sut = new GetProfileHandler(_userRepository);

        var result = await sut.HandleAsync(user.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ProfileResult(user.Id, user.Email.Value, user.DisplayName, user.CreatedAt));
    }

    [Fact] // CA-04 — dois usuários, cada um vê os próprios dados
    public async Task HandleAsync_DoisUsuariosDiferentes_RecebemDadosDiferentes()
    {
        var userA = User.Create(Email.Create("a@exemplo.com").Value, "A", "hash", _timeProvider).Value;
        var userB = User.Create(Email.Create("b@exemplo.com").Value, "B", "hash", _timeProvider).Value;
        _userRepository.GetByIdAsync(userA.Id, Arg.Any<CancellationToken>()).Returns(userA);
        _userRepository.GetByIdAsync(userB.Id, Arg.Any<CancellationToken>()).Returns(userB);
        var sut = new GetProfileHandler(_userRepository);

        var resultA = await sut.HandleAsync(userA.Id, CancellationToken.None);
        var resultB = await sut.HandleAsync(userB.Id, CancellationToken.None);

        resultA.Value.Email.Should().Be("a@exemplo.com");
        resultB.Value.Email.Should().Be("b@exemplo.com");
    }

    [Fact] // BE-16 CA-09: usuário inexistente (conta excluída) — UserNotFound, não exceção
    public async Task HandleAsync_UsuarioInexistente_RetornaUserNotFound()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);
        var sut = new GetProfileHandler(_userRepository);

        var result = await sut.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthErrors.UserNotFound);
    }
}
