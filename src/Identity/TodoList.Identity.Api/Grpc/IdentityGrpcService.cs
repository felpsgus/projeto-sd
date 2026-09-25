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
using TodoList.Identity.Application.Security;
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
    private readonly IAccessTokenValidator _accessTokenValidator;
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
    /// <c>ValidateUser</c>/<c>ValidateToken</c> num ambiente sem banco
    /// configurado — exatamente o cenário que <see cref="UserStoreOptions.InMemoryProvider"/>
    /// existe para suportar (BE-33, CA-11). Resolver só dentro de
    /// <see cref="Login"/>, e só quando o provider é <c>Persisted</c>,
    /// mantém essa independência.
    /// </summary>
    public IdentityGrpcService(
        IUserLookup userLookup,
        IServiceProvider serviceProvider,
        IAccessTokenValidator accessTokenValidator,
        IOptions<UserStoreOptions> userStoreOptions,
        IValidator<ApplicationRegisterUserRequest> registerValidator,
        IValidator<ApplicationChangePasswordRequest> changePasswordValidator,
        ILogger<IdentityGrpcService> logger)
    {
        _userLookup = userLookup;
        _serviceProvider = serviceProvider;
        _accessTokenValidator = accessTokenValidator;
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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            return RespondAndLog(request.UserId, user: null, stopwatch.Elapsed, traceId);
        }

        var user = await _userLookup.FindByIdAsync(userId, context.CancellationToken);

        return RespondAndLog(request.UserId, user, stopwatch.Elapsed, traceId);
    }

    /// <summary>
    /// Implementado em BE-34: valida assinatura, issuer, audience e expiração
    /// do <c>access_token</c> via <see cref="IAccessTokenValidator"/> — os
    /// mesmos <c>TokenValidationParameters</c> configurados em BE-08 (D-31),
    /// nunca uma segunda configuração de validação. Nunca lança nem devolve
    /// status gRPC de erro para token malformado, vazio, expirado ou com
    /// assinatura inválida — sempre <c>OK</c> com <c>valid=false</c> (CA-02,
    /// CA-03, CA-07). Não consulta <see cref="IUserLookup"/>/repositório de
    /// usuário (CA-09): checar <c>IsActive</c> aqui exigiria uma leitura de
    /// banco por requisição autenticada, não só nas que criam tarefa — decisão
    /// registrada nas notas técnicas de BE-34.
    /// </summary>
    public override async Task<ValidateTokenResponse> ValidateToken(ValidateTokenRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        var validation = await _accessTokenValidator.ValidateAsync(request.AccessToken, context.CancellationToken);

        var response = validation.IsValid
            ? new ValidateTokenResponse { Valid = true, UserId = validation.UserId!.Value.ToString() }
            : new ValidateTokenResponse { Valid = false, UserId = string.Empty };

        // CA-08 de BE-34: o token em si nunca aparece em log, só o resultado.
        Log.ValidateTokenCalled(_logger, response.Valid, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return response;
    }

    /// <summary>
    /// Login mínimo via gRPC (BE-33, recorte de BE-09 — D-36): troca e-mail e
    /// senha por um access token. Nunca lança nem devolve status gRPC de erro
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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (_userStoreOptions.Value.Provider == UserStoreOptions.InMemoryProvider)
        {
            return RespondLoginAndLog(succeeded: false, userId: null, accessToken: null, stopwatch.Elapsed, traceId);
        }

        var loginHandler = _serviceProvider.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(request.Email, request.Password, context.CancellationToken);

        return result.IsSuccess
            ? RespondLoginAndLog(succeeded: true, result.Value.UserId, result.Value.AccessToken, stopwatch.Elapsed, traceId)
            : RespondLoginAndLog(succeeded: false, userId: null, accessToken: null, stopwatch.Elapsed, traceId);
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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        var applicationRequest = new ApplicationRegisterUserRequest(request.Email, request.Password, request.DisplayName);
        var validationResult = await _registerValidator.ValidateAsync(applicationRequest, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.RegisterCalled(_logger, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw validationFailed;
        }

        var handler = _serviceProvider.GetRequiredService<RegisterUserHandler>();
        var result = await handler.HandleAsync(applicationRequest, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.RegisterCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.RegisterCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.GetProfileCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw invalid;
        }

        var handler = _serviceProvider.GetRequiredService<GetProfileHandler>();
        var result = await handler.HandleAsync(userId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.GetProfileCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.GetProfileCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.UpdateProfileCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw invalid;
        }

        var handler = _serviceProvider.GetRequiredService<UpdateProfileHandler>();
        var result = await handler.HandleAsync(userId, request.DisplayName, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.UpdateProfileCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.UpdateProfileCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return ToProfileResponse(result.Value);
    }

    /// <summary>
    /// Troca de senha do usuário autenticado (BE-15, RN-AUTH-04/21). A
    /// revogação de sessões (RN-AUTH-19) fica para a Fase 4 (BE-10/BE-11) —
    /// ver nota técnica de <see cref="ChangePasswordHandler"/>.
    /// </summary>
    public override async Task<Empty> ChangePassword(ProtoChangePasswordRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.ChangePasswordCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw invalid;
        }

        var applicationRequest = new ApplicationChangePasswordRequest(userId, request.CurrentPassword, request.NewPassword);
        var validationResult = await _changePasswordValidator.ValidateAsync(applicationRequest, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.ChangePasswordCalled(_logger, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

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

            Log.ChangePasswordCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.ChangePasswordCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

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
        var traceId = context.RequestHeaders.GetValue("traceparent") ?? string.Empty;

        if (!Guid.TryParse(request.UserId, out var userId))
        {
            var invalid = AuthErrors.UserNotFound.ToRpcException();
            Log.DeleteAccountCalled(_logger, invalid.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

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

            Log.DeleteAccountCalled(_logger, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.DeleteAccountCalled(_logger, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return new Empty();
    }

    private static ProfileResponse ToProfileResponse(ProfileResult profile) => new()
    {
        Id = profile.Id.ToString(),
        Email = profile.Email,
        DisplayName = profile.DisplayName,
        CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.CreatedAt, DateTimeKind.Utc)),
    };

    private ValidateUserResponse RespondAndLog(string rawUserId, UserLookupResult? user, TimeSpan elapsed, string traceId)
    {
        var response = user is null
            ? new ValidateUserResponse { Exists = false, Active = false, DisplayName = string.Empty }
            : new ValidateUserResponse { Exists = true, Active = user.Active, DisplayName = user.DisplayName };

        // CA-10 — log estruturado sem dado sensível: userId, exists, active e
        // duração, nunca e-mail, hash de senha ou display_name na entrada de log.
        // traceId (BE-27, CA-13): repassado pelo Tasks via metadata gRPC
        // ("traceparent") para correlacionar os dois lados da chamada; vazio
        // quando o chamador não o envia.
        Log.ValidateUserCalled(_logger, rawUserId, response.Exists, response.Active, elapsed.TotalMilliseconds, traceId);

        return response;
    }

    private LoginResponse RespondLoginAndLog(bool succeeded, Guid? userId, AccessToken? accessToken, TimeSpan elapsed, string traceId)
    {
        var response = succeeded
            ? new LoginResponse
            {
                Succeeded = true,
                AccessToken = accessToken!.Token,
                ExpiresAt = Timestamp.FromDateTimeOffset(accessToken.ExpiresAt),
                UserId = userId!.Value.ToString(),
            }
            : new LoginResponse { Succeeded = false };

        // CA-06 de BE-33: nunca e-mail, senha, hash ou token no log — só
        // userId (quando resolvido), succeeded e duração.
        Log.LoginCalled(_logger, succeeded, userId, elapsed.TotalMilliseconds, traceId);

        return response;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ValidateUser: userId={UserId}, exists={Exists}, active={Active}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void ValidateUserCalled(ILogger logger, string userId, bool exists, bool active, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ValidateToken: valid={Valid}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void ValidateTokenCalled(ILogger logger, bool valid, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Login: succeeded={Succeeded}, userId={UserId}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void LoginCalled(ILogger logger, bool succeeded, Guid? userId, double durationMs, string traceId);

        // BE-07/14/15/16: nunca e-mail, senha, hash ou displayName no log —
        // só o status gRPC e a duração, mesmo padrão dos RPCs acima.
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Register: statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void RegisterCalled(ILogger logger, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "GetProfile: statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void GetProfileCalled(ILogger logger, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "UpdateProfile: statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void UpdateProfileCalled(ILogger logger, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ChangePassword: statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void ChangePasswordCalled(ILogger logger, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "DeleteAccount: statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void DeleteAccountCalled(ILogger logger, StatusCode statusCode, double durationMs, string traceId);
    }
}
