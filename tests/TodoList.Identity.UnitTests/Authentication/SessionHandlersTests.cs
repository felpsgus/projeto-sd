using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.UnitTests.Sessions;
using TodoList.SharedKernel;
using Xunit;

namespace TodoList.Identity.UnitTests.Authentication;

/// <summary>
/// <see cref="RefreshSessionHandler"/> e
/// <see cref="LogoutAllHandler"/> (BE-10, BE-11) — o handler de refresh com o
/// <see cref="IUserRepository"/> substituído e o serviço de tokens real sobre
/// o repositório em memória.
/// </summary>
public class SessionHandlersTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T10:00:00Z"));
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly RefreshTokenService _refreshTokens;
    private readonly InMemoryRefreshTokenRepository _repository;
    private readonly User _user;

    public SessionHandlersTests()
    {
        (_refreshTokens, _repository) = InMemoryRefreshTokenRepository.CreateService(_time);
        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", "hash-qualquer", _time).Value;
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _tokenService.GenerateAccessToken(Arg.Any<User>())
            .Returns(new AccessToken("jwt-novo", _time.GetUtcNow().AddMinutes(15)));
    }

    [Fact] // BE-10 CA-01, CA-03, CA-04
    public async Task Refresh_TokenValido_DevolveNovoParDeTokens()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);

        var result = await CreateRefreshHandler().HandleAsync(login.Value, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.AccessToken.Token.Should().Be("jwt-novo");
        result.Value.RefreshToken.Value.Should().NotBe(login.Value);
        result.Value.RefreshToken.SessionId.Should().Be(login.SessionId);
    }

    [Fact] // CA-16: causas distintas, mesmo Error
    public async Task Refresh_TodasAsCausasDeFalha_ProduzemOMesmoError()
    {
        var expired = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var revoked = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var reused = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var sut = CreateRefreshHandler();

        await _refreshTokens.RevokeSessionAsync(revoked.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);
        await sut.HandleAsync(reused.Value, CancellationToken.None);
        _time.Advance(TimeSpan.FromDays(8));

        var errors = new[]
        {
            (await sut.HandleAsync(null, CancellationToken.None)).Error,
            (await sut.HandleAsync("nunca-existiu", CancellationToken.None)).Error,
            (await sut.HandleAsync(expired.Value, CancellationToken.None)).Error,
            (await sut.HandleAsync(revoked.Value, CancellationToken.None)).Error,
            (await sut.HandleAsync(reused.Value, CancellationToken.None)).Error,
        };

        errors.Should().AllBeEquivalentTo(AuthErrors.InvalidRefreshToken);
    }

    [Fact] // BE-11 CA-04 / CA-08 / CA-09 via serviço
    public async Task Logout_RevogaSessaoDoDonoEDevolveODesfecho()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);

        (await _refreshTokens.RevokeSessionOfTokenAsync(_user.Id, login.Value, CancellationToken.None)).Should().Be(RevokeSessionOutcome.Revoked);
        (await _refreshTokens.RevokeSessionOfTokenAsync(Guid.NewGuid(), login.Value, CancellationToken.None)).Should().Be(RevokeSessionOutcome.OwnedByAnotherUser);
        (await _refreshTokens.RevokeSessionOfTokenAsync(_user.Id, null, CancellationToken.None)).Should().Be(RevokeSessionOutcome.NotFound);
    }

    [Fact] // BE-11 CA-06
    public async Task LogoutAll_RevogaTudoEComitaUmaVez()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);

        await new LogoutAllHandler(_refreshTokens, unitOfWork).HandleAsync(_user.Id, CancellationToken.None);

        _repository.Tokens.Should().OnlyContain(token => token.RevokedReason == RefreshTokenRevocationReason.Logout);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private RefreshSessionHandler CreateRefreshHandler() => new(_refreshTokens, _userRepository, _tokenService);
}
