using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Api.Grpc;
using TodoList.Identity.Api.Validation;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.UnitTests.Sessions;
using TodoList.SharedKernel;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>RPCs <c>RefreshSession</c>, <c>Logout</c> e <c>LogoutAll</c> do <see cref="IdentityGrpcService"/> (BE-10, BE-11).</summary>
public class IdentityGrpcServiceSessionTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-01T10:00:00Z"));
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly RefreshTokenService _refreshTokens;
    private readonly InMemoryRefreshTokenRepository _repository;
    private readonly User _user;

    public IdentityGrpcServiceSessionTests()
    {
        (_refreshTokens, _repository) = InMemoryRefreshTokenRepository.CreateService(_time);
        _user = User.Create(Email.Create("ada@exemplo.com").Value, "Ada", "hash-qualquer", _time).Value;
        _userRepository.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _tokenService.GenerateAccessToken(Arg.Any<User>()).Returns(new AccessToken("jwt-novo", _time.GetUtcNow().AddMinutes(15)));
    }

    [Fact] // BE-10 CA-01
    public async Task RefreshSession_TokenValido_DevolveNovoParDeTokens()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var sut = CreateService();

        var response = await sut.RefreshSession(new RefreshSessionRequest { RefreshToken = login.Value }, new FakeServerCallContext());

        response.Succeeded.Should().BeTrue();
        response.AccessToken.Should().Be("jwt-novo");
        response.RefreshToken.Should().NotBeNullOrEmpty().And.NotBe(login.Value);
        response.RefreshTokenExpiresAt.ToDateTimeOffset().Should().Be(_time.GetUtcNow().AddDays(7));
    }

    [Theory] // BE-10 CA-15/CA-16: falha é succeeded=false, nunca exceção
    [InlineData("")]
    [InlineData("token-inexistente")]
    public async Task RefreshSession_TokenInvalido_RetornaSucceededFalseSemLancar(string token)
    {
        var sut = CreateService();

        var response = await sut.RefreshSession(new RefreshSessionRequest { RefreshToken = token }, new FakeServerCallContext());

        response.Should().Be(new RefreshSessionResponse { Succeeded = false });
    }

    [Fact] // FE-06 CA-12: revogado por ação do usuário sinaliza revoked=true; expirado/inexistente/reuso não
    public async Task RefreshSession_RevogadoPeloUsuario_SinalizaRevoked()
    {
        var revoked = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        await _refreshTokens.RevokeSessionAsync(revoked.SessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);
        var sut = CreateService();

        var response = await sut.RefreshSession(new RefreshSessionRequest { RefreshToken = revoked.Value }, new FakeServerCallContext());

        response.Should().Be(new RefreshSessionResponse { Succeeded = false, Revoked = true });
    }

    [Fact] // CA-18: o token nunca vai para o log
    public async Task RefreshSession_Log_NuncaContemOTokenApresentadoNemOVolta()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var logger = new CapturingLogger();
        var sut = CreateService(logger: logger);

        var response = await sut.RefreshSession(new RefreshSessionRequest { RefreshToken = login.Value }, new FakeServerCallContext());

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(message =>
            !message.Contains(login.Value, StringComparison.Ordinal)
            && !message.Contains(response.RefreshToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshSession_ProviderInMemory_RetornaSucceededFalse()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var sut = CreateService(provider: UserStoreOptions.InMemoryProvider);

        var response = await sut.RefreshSession(new RefreshSessionRequest { RefreshToken = login.Value }, new FakeServerCallContext());

        response.Succeeded.Should().BeFalse();
    }

    [Fact] // BE-11 CA-01/CA-03
    public async Task Logout_TokenDoUsuario_RevogaASessao()
    {
        var login = await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var sut = CreateService();

        await sut.Logout(new LogoutRequest { UserId = _user.Id.ToString(), RefreshToken = login.Value }, new FakeServerCallContext());

        _repository.Tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    [Fact] // BE-11 CA-08: idempotente
    public async Task Logout_SemToken_ResponderOkSemErro()
    {
        var sut = CreateService();

        var act = async () => await sut.Logout(new LogoutRequest { UserId = _user.Id.ToString() }, new FakeServerCallContext());

        await act.Should().NotThrowAsync();
    }

    [Fact] // BE-11 CA-09/CA-10: token de outro usuário — nada revogado, Warning sem o valor
    public async Task Logout_TokenDeOutroUsuario_NaoRevogaERegistraWarningSemOValor()
    {
        var victim = await _refreshTokens.IssueAsync(Guid.NewGuid(), null, CancellationToken.None);
        var logger = new CapturingLogger();
        var sut = CreateService(logger: logger);

        await sut.Logout(new LogoutRequest { UserId = _user.Id.ToString(), RefreshToken = victim.Value }, new FakeServerCallContext());

        _repository.Tokens.Should().OnlyContain(token => token.RevokedAt == null);
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
        logger.Entries.Should().OnlyContain(entry => !entry.Message.Contains(victim.Value, StringComparison.Ordinal));
    }

    [Fact] // BE-11 CA-06
    public async Task LogoutAll_RevogaTodasAsSessoes()
    {
        await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        await _refreshTokens.IssueAsync(_user.Id, null, CancellationToken.None);
        var sut = CreateService();

        await sut.LogoutAll(new LogoutAllRequest { UserId = _user.Id.ToString() }, new FakeServerCallContext());

        _repository.Tokens.Should().OnlyContain(token => token.RevokedAt != null);
    }

    private IdentityGrpcService CreateService(string provider = UserStoreOptions.PersistedProvider, ILogger<IdentityGrpcService>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RefreshSessionHandler(_refreshTokens, _userRepository, _tokenService));
        services.AddSingleton(_refreshTokens);
        services.AddSingleton(new LogoutAllHandler(_refreshTokens, Substitute.For<IUnitOfWork>()));

        return new IdentityGrpcService(
            Substitute.For<IUserLookup>(),
            services.BuildServiceProvider(),
            Options.Create(new UserStoreOptions { Provider = provider }),
            new RegisterUserRequestValidator(),
            new ChangePasswordRequestValidator(),
            logger ?? NullLogger<IdentityGrpcService>.Instance);
    }

    private sealed class CapturingLogger : ILogger<IdentityGrpcService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IEnumerable<string> Messages => Entries.Select(entry => entry.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
