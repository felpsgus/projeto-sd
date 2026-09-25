using FluentAssertions;
using Grpc.Core;
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
using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Security;
using Xunit;
using ProtoChangePasswordRequest = TodoList.Contracts.Identity.V1.ChangePasswordRequest;

namespace TodoList.Identity.UnitTests;

/// <summary>
/// <see cref="IdentityGrpcService"/> — RPCs da Fase 3 (BE-07/BE-14/BE-15/BE-16:
/// <c>Register</c>, <c>GetProfile</c>, <c>UpdateProfile</c>,
/// <c>ChangePassword</c>, <c>DeleteAccount</c>), com <see cref="IUserRepository"/>/
/// <see cref="IUnitOfWork"/> substituídos. Complementa <c>IdentityGrpcServiceTests</c>
/// (ValidateUser/ValidateToken/Login).
/// </summary>
public class IdentityGrpcServiceAccountManagementTests
{
    private const string RegisteredEmail = "ada@exemplo.com";
    private const string CorrectPassword = "senha-correta-123";

    private readonly IUserLookup _userLookup = Substitute.For<IUserLookup>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAccessTokenValidator _accessTokenValidator = Substitute.For<IAccessTokenValidator>();
    private readonly Pbkdf2PasswordHasher _passwordHasher = new(Options.Create(new PasswordHashingOptions { Iterations = 10 }));
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-01-01T10:00:00Z"));
    private readonly User _registeredUser;

    public IdentityGrpcServiceAccountManagementTests()
    {
        _registeredUser = User.Create(
            Email.Create(RegisteredEmail).Value, "Ada Lovelace", _passwordHasher.Hash(CorrectPassword), _timeProvider).Value;

        _userRepository.GetByIdAsync(_registeredUser.Id, Arg.Any<CancellationToken>()).Returns(_registeredUser);
        _userRepository.GetByIdAsync(Arg.Is<Guid>(id => id != _registeredUser.Id), Arg.Any<CancellationToken>()).Returns((User?)null);
    }

    [Fact] // BE-07, CA-01
    public async Task Register_DadosValidos_RetornaRegisterResponseComIdEmailEDisplayName()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateService();

        var response = await sut.Register(
            new RegisterRequest { Email = "nova@exemplo.com", Password = "senha123", DisplayName = "Nova" },
            new FakeServerCallContext());

        response.Email.Should().Be("nova@exemplo.com");
        response.DisplayName.Should().Be("Nova");
        response.Id.Should().NotBeNullOrEmpty();
    }

    [Fact] // BE-07, CA-05: e-mail duplicado vira RpcException FailedPrecondition (D-35: Conflict -> FailedPrecondition)
    public async Task Register_EmailDuplicado_LancaFailedPrecondition()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateService();

        var act = async () => await sut.Register(
            new RegisterRequest { Email = RegisteredEmail, Password = "senha123", DisplayName = "" },
            new FakeServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        exception.Which.Trailers.Get(TodoList.Identity.Api.ResultMapping.ResultGrpcStatus.ErrorCodeTrailerKey)!.Value
            .Should().Be("auth.email_already_registered");
    }

    [Fact] // BE-07, CA-08: senha fraca vira InvalidArgument com trailer de validação
    public async Task Register_SenhaFraca_LancaInvalidArgumentComErrosPorCampo()
    {
        var sut = CreateService();

        var act = async () => await sut.Register(
            new RegisterRequest { Email = "nova@exemplo.com", Password = "curta", DisplayName = "" },
            new FakeServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // BE-07: nenhum log de Register contém a senha
    public async Task Register_Log_NuncaContemSenha()
    {
        _userRepository.EmailExistsAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(false);
        var logger = new CapturingLogger();
        var sut = CreateService(logger: logger);

        await sut.Register(
            new RegisterRequest { Email = "nova@exemplo.com", Password = "senha123", DisplayName = "" },
            new FakeServerCallContext());

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(m => !m.Contains("senha123", StringComparison.Ordinal));
    }

    [Fact] // BE-14, CA-01
    public async Task GetProfile_UsuarioExistente_RetornaPerfil()
    {
        var sut = CreateService();

        var response = await sut.GetProfile(new GetProfileRequest { UserId = _registeredUser.Id.ToString() }, new FakeServerCallContext());

        response.Email.Should().Be(RegisteredEmail);
        response.DisplayName.Should().Be("Ada Lovelace");
    }

    [Fact] // BE-16, CA-09: user_id não encontrado (conta excluída) vira Unauthenticated, não 500
    public async Task GetProfile_UsuarioInexistente_LancaUnauthenticated()
    {
        var sut = CreateService();

        var act = async () => await sut.GetProfile(new GetProfileRequest { UserId = Guid.NewGuid().ToString() }, new FakeServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact] // BE-26/BE-14: user_id malformado nunca derruba o servidor
    public async Task GetProfile_UserIdMalformado_LancaUnauthenticatedSemExcecaoNaoTratada()
    {
        var sut = CreateService();

        var act = async () => await sut.GetProfile(new GetProfileRequest { UserId = "abc" }, new FakeServerCallContext());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact] // BE-14, CA-05/CA-08
    public async Task UpdateProfile_NomeValido_AtualizaEDevolvePerfil()
    {
        var sut = CreateService();

        var response = await sut.UpdateProfile(
            new UpdateProfileRequest { UserId = _registeredUser.Id.ToString(), DisplayName = "  Novo Nome  " },
            new FakeServerCallContext());

        response.DisplayName.Should().Be("Novo Nome");
    }

    [Fact] // BE-14, CA-06
    public async Task UpdateProfile_NomeVazio_LancaInvalidArgument()
    {
        var sut = CreateService();

        var act = async () => await sut.UpdateProfile(
            new UpdateProfileRequest { UserId = _registeredUser.Id.ToString(), DisplayName = "" },
            new FakeServerCallContext());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // BE-15, CA-01/CA-15: sucesso devolve Empty
    public async Task ChangePassword_DadosValidos_RetornaEmpty()
    {
        var sut = CreateService();

        var response = await sut.ChangePassword(
            new ProtoChangePasswordRequest { UserId = _registeredUser.Id.ToString(), CurrentPassword = CorrectPassword, NewPassword = "senha-nova-456" },
            new FakeServerCallContext());

        response.Should().NotBeNull();
    }

    [Fact] // BE-15, CA-04 + decisão do tech lead: senha atual errada vira InvalidArgument (400), não Unauthenticated —
           // a requisição já está autenticada, o que falhou é um campo do corpo (ver AuthErrors.InvalidCurrentPassword)
    public async Task ChangePassword_SenhaAtualErrada_LancaInvalidArgumentComErroNoCampoCurrentPassword()
    {
        var sut = CreateService();

        var act = async () => await sut.ChangePassword(
            new ProtoChangePasswordRequest { UserId = _registeredUser.Id.ToString(), CurrentPassword = "errada", NewPassword = "senha-nova-456" },
            new FakeServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Which.Trailers.Get(TodoList.Identity.Api.ResultMapping.ResultGrpcStatus.ErrorCodeTrailerKey)!.Value
            .Should().Be("auth.invalid_current_password");

        var validationErrorsJson = exception.Which.Trailers.Get(TodoList.Identity.Api.ResultMapping.ResultGrpcStatus.ValidationErrorsTrailerKey)!.Value;
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(validationErrorsJson)!
            .Should().ContainKey("CurrentPassword", "o front precisa saber em qual campo pôr a mensagem");
    }

    [Fact] // BE-15, CA-05: nova senha fraca — validação de borda roda antes do handler
    public async Task ChangePassword_NovaSenhaFraca_LancaInvalidArgument()
    {
        var sut = CreateService();

        var act = async () => await sut.ChangePassword(
            new ProtoChangePasswordRequest { UserId = _registeredUser.Id.ToString(), CurrentPassword = CorrectPassword, NewPassword = "curta" },
            new FakeServerCallContext());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact] // BE-15, CA-14: nenhum log contém a senha atual nem a nova
    public async Task ChangePassword_Log_NuncaContemSenhas()
    {
        var logger = new CapturingLogger();
        var sut = CreateService(logger: logger);

        await sut.ChangePassword(
            new ProtoChangePasswordRequest { UserId = _registeredUser.Id.ToString(), CurrentPassword = CorrectPassword, NewPassword = "senha-nova-456" },
            new FakeServerCallContext());

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(m =>
            !m.Contains(CorrectPassword, StringComparison.Ordinal) && !m.Contains("senha-nova-456", StringComparison.Ordinal));
    }

    [Fact] // BE-16, CA-01: exclusão com senha correta retorna Empty
    public async Task DeleteAccount_SenhaCorreta_RetornaEmpty()
    {
        var sut = CreateService();

        var response = await sut.DeleteAccount(
            new DeleteAccountRequest { UserId = _registeredUser.Id.ToString(), Password = CorrectPassword },
            new FakeServerCallContext());

        response.Should().NotBeNull();
        _userRepository.Received(1).Remove(_registeredUser);
    }

    [Fact] // BE-16, CA-10 + decisão do tech lead: senha incorreta vira InvalidArgument (400), não Unauthenticated, e não remove nada
    public async Task DeleteAccount_SenhaIncorreta_LancaInvalidArgumentComErroNoCampoPasswordSemRemover()
    {
        var sut = CreateService();

        var act = async () => await sut.DeleteAccount(
            new DeleteAccountRequest { UserId = _registeredUser.Id.ToString(), Password = "errada" },
            new FakeServerCallContext());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        exception.Which.Trailers.Get(TodoList.Identity.Api.ResultMapping.ResultGrpcStatus.ErrorCodeTrailerKey)!.Value
            .Should().Be("auth.invalid_current_password");

        var validationErrorsJson = exception.Which.Trailers.Get(TodoList.Identity.Api.ResultMapping.ResultGrpcStatus.ValidationErrorsTrailerKey)!.Value;
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(validationErrorsJson)!
            .Should().ContainKey("Password");

        _userRepository.DidNotReceive().Remove(Arg.Any<User>());
    }

    [Fact] // BE-16, CA-14: nenhum log contém a senha
    public async Task DeleteAccount_Log_NuncaContemSenha()
    {
        var logger = new CapturingLogger();
        var sut = CreateService(logger: logger);

        await sut.DeleteAccount(
            new DeleteAccountRequest { UserId = _registeredUser.Id.ToString(), Password = CorrectPassword },
            new FakeServerCallContext());

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().OnlyContain(m => !m.Contains(CorrectPassword, StringComparison.Ordinal));
    }

    private IdentityGrpcService CreateService(ILogger<IdentityGrpcService>? logger = null)
    {
        var registerHandler = new RegisterUserHandler(_userRepository, _unitOfWork, _passwordHasher, _timeProvider);
        var getProfileHandler = new GetProfileHandler(_userRepository);
        var updateProfileHandler = new UpdateProfileHandler(_userRepository, _unitOfWork, _timeProvider);
        var changePasswordHandler = new ChangePasswordHandler(_userRepository, _unitOfWork, _passwordHasher, _timeProvider);
        var deleteAccountHandler = new DeleteAccountHandler(_userRepository, _unitOfWork, _passwordHasher);

        var services = new ServiceCollection();
        services.AddSingleton(registerHandler);
        services.AddSingleton(getProfileHandler);
        services.AddSingleton(updateProfileHandler);
        services.AddSingleton(changePasswordHandler);
        services.AddSingleton(deleteAccountHandler);
        var serviceProvider = services.BuildServiceProvider();

        var userStoreOptions = Options.Create(new UserStoreOptions { Provider = UserStoreOptions.PersistedProvider });

        return new IdentityGrpcService(
            _userLookup,
            serviceProvider,
            _accessTokenValidator,
            userStoreOptions,
            new RegisterUserRequestValidator(),
            new ChangePasswordRequestValidator(),
            logger ?? NullLogger<IdentityGrpcService>.Instance);
    }

    private sealed class CapturingLogger : ILogger<IdentityGrpcService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
