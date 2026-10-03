using System.Diagnostics;
using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Api.ResultMapping;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.SharedKernel;
using ApplicationChangePasswordRequest = TodoList.Identity.Application.Authentication.ChangePasswordRequest;
using ApplicationRegisterUserRequest = TodoList.Identity.Application.Authentication.RegisterUserRequest;
using ProtoChangePasswordRequest = TodoList.Contracts.Identity.V1.ChangePasswordRequest;

namespace TodoList.Identity.Api.Grpc;

/// <summary>
/// Servidor gRPC do Identity Service (BE-26) — a única implementação do
/// contrato <c>identity.v1.IdentityService</c> (BE-25). Registrado em
/// <c>Program.cs</c> via <c>AddGrpc()</c> + <c>MapGrpcService&lt;&gt;()</c>,
/// nunca por Controller.
/// </summary>
public sealed partial class IdentityGrpcService : IdentityService.IdentityServiceBase
{
    private readonly IUserLookup _userLookup;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<UserStoreOptions> _userStoreOptions;
    private readonly IValidator<ApplicationRegisterUserRequest> _registerValidator;
    private readonly IValidator<ApplicationChangePasswordRequest> _changePasswordValidator;
    private readonly ILogger<IdentityGrpcService> _logger;

    /// <summary>
    /// <see cref="LoginHandler"/> chega por <see cref="IServiceProvider"/>
    /// (o do próprio escopo da requisição, resolvido preguiçosamente — nunca
    /// injetado direto no construtor). Ele depende de <c>IUserRepository</c>,
    /// que por sua vez depende do <c>IdentityDbContext</c>: resolvê-lo eager
    /// forçaria o Identity a exigir <c>ConnectionStrings:IdentityDb</c>
    /// mesmo com <c>UserStore:Provider=InMemory</c>, quebrando
    /// <c>ValidateUser</c> num ambiente sem banco
    /// configurado — exatamente o cenário que <see cref="UserStoreOptions.InMemoryProvider"/>
    /// existe para suportar (BE-33, CA-11). Resolver só dentro de
    /// <see cref="Login"/>, e só quando o provider é <c>Persisted</c>,
    /// mantém essa independência.
    /// </summary>
    public IdentityGrpcService(
        IUserLookup userLookup,
        IServiceProvider serviceProvider,
        IOptions<UserStoreOptions> userStoreOptions,
        IValidator<ApplicationRegisterUserRequest> registerValidator,
        IValidator<ApplicationChangePasswordRequest> changePasswordValidator,
        ILogger<IdentityGrpcService> logger)
    {
        _userLookup = userLookup;
        _serviceProvider = serviceProvider;
        _userStoreOptions = userStoreOptions;
        _registerValidator = registerValidator;
        _changePasswordValidator = changePasswordValidator;
        _logger = logger;
    }

    /// <summary>
    /// Valida o dono de uma tarefa (RN-AUTZ-01, RN-USER-04). Nunca lança nem
    /// devolve erro gRPC para "usuário não existe" ou "id malformado" — os
    /// dois são resposta negativa de negócio, com status <c>OK</c> (CA-07,
    /// CA-08 de BE-26): um <c>INVALID_ARGUMENT</c> obrigaria o cliente a
    /// tratar dois caminhos de falha para o mesmo desfecho.
    /// </summary>
    public override async Task<ValidateUserResponse> ValidateUser(ValidateUserRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return RespondAndLog(request.UserId, user: null, stopwatch.Elapsed);
        }

        var user = await _userLookup.FindByIdAsync(userId, context.CancellationToken);

        return RespondAndLog(request.UserId, user, stopwatch.Elapsed);
    }

    /// <summary>
    /// Login via gRPC (BE-33/BE-09/BE-10): troca e-mail e senha por um access
    /// token e um refresh token de uma sessão nova. Nunca lança nem devolve status gRPC de erro
    /// por conteúdo do request (CA-12) — inclusive e-mail vazio, malformado
    /// ou senha vazia resultam em <c>succeeded=false</c>, como qualquer
    /// credencial inválida.
    ///
    /// <para>
    /// <b>InMemory não suporta login (CA-11).</b> A decisão sobre o provider
    /// de usuários é da borda (Api), não da Application: com
    /// <c>UserStore:Provider=InMemory</c> este método responde
    /// <c>succeeded=false</c> sem consultar <see cref="LoginHandler"/> nem
    /// qualquer repositório — não há senha/hash associado ao seed em memória.
    /// O aviso de que o modo não suporta login é emitido uma única vez, na
    /// inicialização (<c>Program.cs</c>), não a cada chamada.
    /// </para>
    /// </summary>
    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (_userStoreOptions.Value.Provider == UserStoreOptions.InMemoryProvider)
        {
            return RespondLoginAndLog(login: null, retryAfter: null, stopwatch.Elapsed);
        }

        var loginHandler = _serviceProvider.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(request.Email, request.Password, context.CancellationToken);

        return RespondLoginAndLog(result.IsSuccess ? result.Value : null, LockoutRetryAfter(result), stopwatch.Elapsed);
    }

    /// <summary>BE-12: tempo restante quando a falha é o bloqueio por tentativas; senão <c>null</c>.</summary>
    private static TimeSpan? LockoutRetryAfter(Result<LoginResult> result) =>
        result.IsFailure && result.Error.Code == AuthErrors.TooManyAttemptsCode ? result.Error.RetryAfter : null;

    /// <summary>
    /// Renovação de sessão (BE-10). Qualquer falha — inclusive provider
    /// InMemory, que não tem sessões — é <c>succeeded=false</c>, nunca status
    /// de erro por causa do token (RN-AUTH-17, CA-16).
    /// </summary>
    public override async Task<RefreshSessionResponse> RefreshSession(RefreshSessionRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        RefreshSessionResult? refreshed = null;

        if (_userStoreOptions.Value.Provider != UserStoreOptions.InMemoryProvider)
        {
            var handler = _serviceProvider.GetRequiredService<RefreshSessionHandler>();
            var result = await handler.HandleAsync(request.RefreshToken, context.CancellationToken);
            refreshed = result.IsSuccess ? result.Value : null;
        }

        // Nunca o token (nem o novo, nem o apresentado) no log (RN-AUTH-20).
        if (refreshed is null)
        {
            // Warning: token inexistente, expirado, revogado ou reapresentado (reuso, RN-AUTH-17) — o
            // chamador não distingue a causa, então o log também não.
            Log.RefreshSessionRejected(_logger, stopwatch.Elapsed.TotalMilliseconds);
        }
        else
        {
            Log.RefreshSessionCalled(_logger, true, stopwatch.Elapsed.TotalMilliseconds);
        }

        return refreshed is null
            ? new RefreshSessionResponse { Succeeded = false }
            : new RefreshSessionResponse
            {
                Succeeded = true,
                AccessToken = refreshed.AccessToken.Token,
                ExpiresAt = Timestamp.FromDateTimeOffset(refreshed.AccessToken.ExpiresAt),
                RefreshToken = refreshed.RefreshToken.Value,
                RefreshTokenExpiresAt = Timestamp.FromDateTimeOffset(refreshed.RefreshToken.ExpiresAt),
            };
    }

    /// <summary>
    /// Logout da sessão do refresh token (BE-11, RN-AUTH-12). Idempotente. Token
    /// de outro usuário não revoga nada e gera um aviso sem o valor (CA-09/CA-10).
    /// </summary>
    public override async Task<Empty> Logout(LogoutRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.LogoutCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        if (_userStoreOptions.Value.Provider != UserStoreOptions.InMemoryProvider)
        {
            var refreshTokens = _serviceProvider.GetRequiredService<RefreshTokenService>();
            var outcome = await refreshTokens.RevokeSessionOfTokenAsync(userId, request.RefreshToken, context.CancellationToken);

            if (outcome == RevokeSessionOutcome.OwnedByAnotherUser)
            {
                Log.LogoutWithForeignToken(_logger, userId);
            }
        }

        Log.LogoutCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new Empty();
    }

    /// <summary>Revoga todas as sessões do usuário (BE-11, RN-AUTH-19).</summary>
    public override async Task<Empty> LogoutAll(LogoutAllRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.LogoutAllCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        if (_userStoreOptions.Value.Provider != UserStoreOptions.InMemoryProvider)
        {
            var handler = _serviceProvider.GetRequiredService<LogoutAllHandler>();
            await handler.HandleAsync(userId, context.CancellationToken);
        }

        Log.LogoutAllCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new Empty();
    }

    /// <summary>
    /// Cadastro de usuário (BE-07, RN-AUTH-01/02/03/04/06/07). Ao contrário de
    /// <see cref="Login"/>, aqui e-mail duplicado e senha fora da política
    /// viram <see cref="RpcException"/> via <see cref="ResultGrpcStatus"/>
    /// (D-35) — não há motivo para esconder a causa, e o vazamento de
    /// existência é aceitável e desejável (nota técnica de BE-07).
    /// </summary>
    public override async Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        var applicationRequest = new ApplicationRegisterUserRequest(request.Email, request.Password, request.DisplayName);
        var validationResult = await _registerValidator.ValidateAsync(applicationRequest, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.RegisterCalled(_logger, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw validationFailed;
        }

        var handler = _serviceProvider.GetRequiredService<RegisterUserHandler>();
        var result = await handler.HandleAsync(applicationRequest, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.RegisterCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.RegisterCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new RegisterResponse
        {
            Id = result.Value.Id.ToString(),
            Email = result.Value.Email,
            DisplayName = result.Value.DisplayName,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(result.Value.CreatedAt, DateTimeKind.Utc)),
        };
    }

    /// <summary>
    /// Perfil do usuário autenticado (BE-14, RN-USER-01). <c>user_id</c> vem
    /// do <c>sub</c> do token, já validado pelo Gateway (D-38) — este RPC
    /// não valida token, só confia no id recebido.
    /// </summary>
    public override async Task<ProfileResponse> GetProfile(GetProfileRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.GetProfileCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        var handler = _serviceProvider.GetRequiredService<GetProfileHandler>();
        var result = await handler.HandleAsync(userId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.GetProfileCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.GetProfileCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return ToProfileResponse(result.Value);
    }

    /// <summary>
    /// Altera o nome de exibição do usuário autenticado (BE-14, RN-USER-02).
    /// A mensagem <see cref="UpdateProfileRequest"/> não tem campo de e-mail —
    /// RN-USER-03 é garantida por construção, não por checagem em runtime.
    /// </summary>
    public override async Task<ProfileResponse> UpdateProfile(UpdateProfileRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.UpdateProfileCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        var handler = _serviceProvider.GetRequiredService<UpdateProfileHandler>();
        var result = await handler.HandleAsync(userId, request.DisplayName, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.UpdateProfileCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.UpdateProfileCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return ToProfileResponse(result.Value);
    }

    /// <summary>
    /// Troca de senha do usuário autenticado (BE-15, RN-AUTH-04/21). Revoga as
    /// sessões do usuário (RN-AUTH-19) — ver <see cref="ChangePasswordHandler"/>.
    /// </summary>
    public override async Task<Empty> ChangePassword(ProtoChangePasswordRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.ChangePasswordCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        var applicationRequest = new ApplicationChangePasswordRequest(userId, request.CurrentPassword, request.NewPassword);
        var validationResult = await _changePasswordValidator.ValidateAsync(applicationRequest, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.ChangePasswordCalled(_logger, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw validationFailed;
        }

        var handler = _serviceProvider.GetRequiredService<ChangePasswordHandler>();
        var result = await handler.HandleAsync(applicationRequest, context.CancellationToken);

        if (result.IsFailure)
        {
            // Decisão do tech lead (AuthErrors.InvalidCurrentPassword): o
            // error-code continua auth.invalid_current_password (nunca
            // validation.failed), mas o campo do formulário (currentPassword)
            // viaja no trailer validation-errors para o Gateway repassar ao
            // ProblemDetails, sem exigir do frontend nenhuma lógica especial
            // além da que já usa para qualquer 400 por campo.
            var failure = result.Error == AuthErrors.InvalidCurrentPassword
                ? AuthErrors.InvalidCurrentPassword.ToRpcException(
                    new Dictionary<string, string[]> { [nameof(ApplicationChangePasswordRequest.CurrentPassword)] = [AuthErrors.InvalidCurrentPassword.Message] })
                : result.ToRpcException();

            Log.ChangePasswordCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.ChangePasswordCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new Empty();
    }

    /// <summary>
    /// Exclusão da própria conta (BE-16, RN-USER-05). A remoção das tarefas
    /// acontece em cascata no banco (D-27) — ver nota técnica de
    /// <see cref="DeleteAccountHandler"/>; este método não chama o Tasks
    /// Service.
    /// </summary>
    public override async Task<Empty> DeleteAccount(DeleteAccountRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.DeleteAccountCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalid;
        }

        var handler = _serviceProvider.GetRequiredService<DeleteAccountHandler>();
        var result = await handler.HandleAsync(userId, request.Password, context.CancellationToken);

        if (result.IsFailure)
        {
            // Mesma decisão do tech lead de ChangePassword (ver comentário lá):
            // error-code auth.invalid_current_password preservado, campo
            // "password" (o nome do campo de confirmação de BE-16) anexado ao
            // trailer validation-errors.
            var failure = result.Error == AuthErrors.InvalidCurrentPassword
                ? AuthErrors.InvalidCurrentPassword.ToRpcException(
                    new Dictionary<string, string[]> { [nameof(request.Password)] = [AuthErrors.InvalidCurrentPassword.Message] })
                : result.ToRpcException();

            Log.DeleteAccountCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.DeleteAccountCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new Empty();
    }

    private static ProfileResponse ToProfileResponse(ProfileResult profile) => new()
    {
        Id = profile.Id.ToString(),
        Email = profile.Email,
        DisplayName = profile.DisplayName,
        CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc)),
    };

    private ValidateUserResponse RespondAndLog(string rawUserId, UserLookupResult? user, TimeSpan elapsed)
    {
        var response = user is null
            ? new ValidateUserResponse { Exists = false, DisplayName = string.Empty }
            : new ValidateUserResponse { Exists = true, DisplayName = user.DisplayName };

        // CA-10 — log estruturado sem dado sensível: userId, exists e
        // duração, nunca e-mail, hash de senha ou display_name na entrada de log.
        Log.ValidateUserCalled(_logger, rawUserId, response.Exists, elapsed.TotalMilliseconds);

        return response;
    }

    private LoginResponse RespondLoginAndLog(LoginResult? login, TimeSpan? retryAfter, TimeSpan elapsed)
    {
        var response = login is not null
            ? new LoginResponse
            {
                Succeeded = true,
                AccessToken = login.AccessToken.Token,
                ExpiresAt = Timestamp.FromDateTimeOffset(login.AccessToken.ExpiresAt),
                UserId = login.UserId.ToString(),
                RefreshToken = login.RefreshToken.Value,
                RefreshTokenExpiresAt = Timestamp.FromDateTimeOffset(login.RefreshToken.ExpiresAt),
            }
            : new LoginResponse
            {
                Succeeded = false,
                LockedOut = retryAfter is not null,
                RetryAfterSeconds = retryAfter is { } wait ? (int)Math.Max(1, Math.Ceiling(wait.TotalSeconds)) : 0,
            };

        // CA-06 de BE-33/CA-14 de BE-12: nunca senha, hash ou token (access ou
        // refresh) no log — só userId (quando resolvido), succeeded, bloqueio e duração.
        if (retryAfter is not null)
        {
            Log.LoginLockedOut(_logger, elapsed.TotalMilliseconds);
        }
        else
        {
            Log.LoginCalled(_logger, login is not null, false, login?.UserId, elapsed.TotalMilliseconds);
        }

        return response;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ValidateUser: userId={UserId}, exists={Exists}, durationMs={DurationMs}")]
        public static partial void ValidateUserCalled(ILogger logger, string userId, bool exists, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Login: succeeded={Succeeded}, lockedOut={LockedOut}, userId={UserId}, durationMs={DurationMs}")]
        public static partial void LoginCalled(ILogger logger, bool succeeded, bool lockedOut, Guid? userId, double durationMs);

        // BE-12/BE-24: bloqueio de login por tentativas é Warning (ADR 0002).
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Login bloqueado por excesso de tentativas: durationMs={DurationMs}")]
        public static partial void LoginLockedOut(ILogger logger, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "RefreshSession recusado (token inválido, expirado ou reutilizado): durationMs={DurationMs}")]
        public static partial void RefreshSessionRejected(ILogger logger, double durationMs);

        // BE-10/BE-11: nunca o valor de um refresh token no log.
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "RefreshSession: succeeded={Succeeded}, durationMs={DurationMs}")]
        public static partial void RefreshSessionCalled(ILogger logger, bool succeeded, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Logout: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void LogoutCalled(ILogger logger, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "LogoutAll: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void LogoutAllCalled(ILogger logger, StatusCode statusCode, double durationMs);

        // BE-11 CA-10: um usuário tentou encerrar a sessão de outro. Só o id de quem tentou.
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Logout com refresh token de outro usuário ignorado: userId={UserId}")]
        public static partial void LogoutWithForeignToken(ILogger logger, Guid userId);

        // BE-07/14/15/16: nunca e-mail, senha, hash ou displayName no log —
        // só o status gRPC e a duração, mesmo padrão dos RPCs acima.
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Register: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void RegisterCalled(ILogger logger, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "GetProfile: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void GetProfileCalled(ILogger logger, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "UpdateProfile: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void UpdateProfileCalled(ILogger logger, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ChangePassword: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void ChangePasswordCalled(ILogger logger, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "DeleteAccount: statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void DeleteAccountCalled(ILogger logger, StatusCode statusCode, double durationMs);
    }
}
