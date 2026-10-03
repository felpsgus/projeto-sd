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
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using TodoList.Identity.UnitTests.Authentication;
using TodoList.Identity.UnitTests.Security;
using TodoList.Identity.UnitTests.Sessions;
using Xunit;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="IdentityGrpcService"/> com <see cref="IUserLookup"/>,
/// <see cref="IUserRepository"/> substituídos — BE-26 (CA-05 a CA-09) e BE-33.
/// </summary>
public class IdentityGrpcServiceTests : IDisposable
{
    private static readonly Guid _existingUserId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid _unknownUserId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private const string CorrectPassword = "senha-correta-123";
    private const string RegisteredEmail = "ada@exemplo.com";

    private readonly IUserLookup _userLookup = Substitute.For<IUserLookup>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly TestRsaKeyFile _jwtKeyFile = TestRsaKeyFile.Create();
    private readonly User _registeredUser;

    public IdentityGrpcServiceTests()
    {
        _userLookup
            .FindByIdAsync(_existingUserId, Arg.Any<CancellationToken>())
            .Returns(new UserLookupResult(DisplayName: "Ada Lovelace"));

        _userLookup
            .FindByIdAsync(_unknownUserId, Arg.Any<CancellationToken>())
            .Returns((UserLookupResult?)null);

        _registeredUser = User.Create(
            Email.Create(RegisteredEmail).Value, "Ada Lovelace", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;

        _userRepository
            .GetByEmailAsync(Arg.Is<Email>(e => e.Value == RegisteredEmail), Arg.Any<CancellationToken>())
            .Returns(_registeredUser);

        _userRepository
            .GetByEmailAsync(Arg.Is<Email>(e => e.Value != RegisteredEmail), Arg.Any<CancellationToken>())
            .Returns((User?)null);
    }

    [Fact] // CA-05
    public async Task ValidateUser_UsuarioExistente_RetornaExistsTrue()
    {
        var sut = CreateService();
        var request = new ValidateUserRequest { UserId = _existingUserId.ToString() };

        var response = await sut.ValidateUser(request, new FakeServerCallContext());

        response.Exists.Should().BeTrue();
        response.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact] // CA-07
    public async Task ValidateUser_UsuarioInexistente_RetornaExistsFalseEDisplayNameVazio()
    {
        var sut = CreateService();
        var request = new ValidateUserRequest { UserId = _unknownUserId.ToString() };

        var response = await sut.ValidateUser(request, new FakeServerCallContext());

        response.Exists.Should().BeFalse();
        response.DisplayName.Should().Be(string.Empty);
    }

    [Theory] // CA-08
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task ValidateUser_UserIdMalformado_RetornaRespostaNegativaSemLancar(string malformedUserId)
    {
        var sut = CreateService();
        var request = new ValidateUserRequest { UserId = malformedUserId };

        var act = async () => await sut.ValidateUser(request, new FakeServerCallContext());

        var response = await act.Should().NotThrowAsync();
        response.Subject.Exists.Should().BeFalse();
        response.Subject.DisplayName.Should().Be(string.Empty);
    }

    [Fact] // CA-09
    public async Task ValidateUser_Resposta_NaoContemCampoAlemDosDoisDoContrato()
    {
        var sut = CreateService();
        var request = new ValidateUserRequest { UserId = _existingUserId.ToString() };

        var response = await sut.ValidateUser(request, new FakeServerCallContext());

        var descriptorFields = ValidateUserResponse.Descriptor.Fields.InFieldNumberOrder();
        descriptorFields.Select(field => field.Name).Should().BeEquivalentTo("exists", "display_name");
    }

    [Fact] // BE-33, CA-01 — login com credenciais corretas
    public async Task Login_CredenciaisCorretas_RetornaSucceededTrueComAccessToken()
    {
        var sut = CreateService();

        var response = await sut.Login(new LoginRequest { Email = RegisteredEmail, Password = CorrectPassword }, new FakeServerCallContext());

        response.Succeeded.Should().BeTrue();
        response.AccessToken.Should().NotBeNullOrEmpty();
        response.UserId.Should().Be(_registeredUser.Id.ToString());
        response.ExpiresAt.Should().NotBeNull();
        response.RefreshToken.Should().NotBeNullOrEmpty("BE-10: cada login abre uma sessão com refresh token");
        response.RefreshTokenExpiresAt.ToDateTimeOffset().Should().Be(_timeProvider.GetUtcNow().AddDays(7));
    }

    [Fact] // BE-10/BE-09 CA-10b — falha de login nunca carrega refresh token
    public async Task Login_Falha_NaoCarregaRefreshToken()
    {
        var sut = CreateService();

        var response = await sut.Login(new LoginRequest { Email = RegisteredEmail, Password = "senha-errada" }, new FakeServerCallContext());

        response.RefreshToken.Should().BeEmpty();
        response.RefreshTokenExpiresAt.Should().BeNull();
    }

    [Fact] // BE-33, CA-02/CA-03 — senha errada e e-mail inexistente produzem a mesma resposta negativa
    public async Task Login_SenhaErradaOuEmailInexistente_RetornaMesmaRespostaNegativa()
    {
        var sut = CreateService();

        var senhaErrada = await sut.Login(new LoginRequest { Email = RegisteredEmail, Password = "senha-errada" }, new FakeServerCallContext());
        var emailInexistente = await sut.Login(new LoginRequest { Email = "ninguem@exemplo.com", Password = CorrectPassword }, new FakeServerCallContext());

        senhaErrada.Should().Be(emailInexistente);
        senhaErrada.Succeeded.Should().BeFalse();
        senhaErrada.AccessToken.Should().BeEmpty();
        senhaErrada.UserId.Should().BeEmpty();
    }

    [Fact] // BE-33, CA-12 — request malformado nunca lança
    public async Task Login_EmailVazio_NaoLancaERetornaSucceededFalse()
    {
        var sut = CreateService();

        var act = async () => await sut.Login(new LoginRequest { Email = string.Empty, Password = string.Empty }, new FakeServerCallContext());

        (await act.Should().NotThrowAsync()).Which.Succeeded.Should().BeFalse();
    }

    [Fact] // BE-33, CA-06 — nenhum log de Login contém e-mail, senha ou token
    public async Task Login_Log_NuncaContemEmailSenhaOuToken()
    {
        var logger = new CapturingLogger<IdentityGrpcService>();
        var sut = CreateService(logger: logger);

        var response = await sut.Login(new LoginRequest { Email = RegisteredEmail, Password = CorrectPassword }, new FakeServerCallContext());

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(message =>
            !message.Contains(RegisteredEmail, StringComparison.Ordinal)
            && !message.Contains(CorrectPassword, StringComparison.Ordinal)
            && !message.Contains(response.AccessToken, StringComparison.Ordinal)
            && !message.Contains(response.RefreshToken, StringComparison.Ordinal));
    }

    [Fact] // BE-33, CA-11 — UserStore:Provider=InMemory nunca consulta o repositório
    public async Task Login_ProviderInMemory_RetornaSucceededFalseSemConsultarRepositorio()
    {
        var sut = CreateService(provider: UserStoreOptions.InMemoryProvider);

        var response = await sut.Login(new LoginRequest { Email = RegisteredEmail, Password = CorrectPassword }, new FakeServerCallContext());

        response.Succeeded.Should().BeFalse();
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        _jwtKeyFile.Dispose();
        GC.SuppressFinalize(this);
    }

    private IdentityGrpcService CreateService(string provider = UserStoreOptions.PersistedProvider, ILogger<IdentityGrpcService>? logger = null)
    {
        var jwtOptions = Options.Create(new JwtOptions { Issuer = "todolist-identity", Audience = "todolist", PrivateKeyPath = _jwtKeyFile.Path });
        var signingKeyProvider = new RsaSigningKeyProvider(jwtOptions);
        var tokenService = new JwtTokenService(jwtOptions, _timeProvider, signingKeyProvider);
        var dummyPasswordHash = new DummyPasswordHash(_passwordHasher);
        var (refreshTokens, _) = InMemoryRefreshTokenRepository.CreateService(_timeProvider);
        var loginHandler = new LoginHandler(
            _userRepository, _passwordHasher, tokenService, dummyPasswordHash, refreshTokens, new InMemoryLoginAttemptStore(), new LockoutOptions(), _timeProvider);
        var userStoreOptions = Options.Create(new UserStoreOptions { Provider = provider });

        // IdentityGrpcService resolve LoginHandler preguiçosamente via
        // IServiceProvider (ver XML doc do construtor) — aqui um provider
        // mínimo só com o LoginHandler já pronto, sem precisar de um
        // IUserRepository real por trás.
        var services = new ServiceCollection();
        services.AddSingleton(loginHandler);
        var serviceProvider = services.BuildServiceProvider();

        return new IdentityGrpcService(
            _userLookup,
            serviceProvider,
            userStoreOptions,
            new RegisterUserRequestValidator(),
            new ChangePasswordRequestValidator(),
            logger ?? NullLogger<IdentityGrpcService>.Instance);
    }

    /// <summary><see cref="ILogger{TCategoryName}"/> mínimo que grava as mensagens formatadas, para CA-06/CA-08 (não expor dado sensível).</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
