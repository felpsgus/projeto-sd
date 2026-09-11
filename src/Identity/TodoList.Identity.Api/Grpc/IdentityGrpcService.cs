using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Identity.Api.Configuration;
using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;

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
        ILogger<IdentityGrpcService> logger)
    {
        _userLookup = userLookup;
        _serviceProvider = serviceProvider;
        _accessTokenValidator = accessTokenValidator;
        _userStoreOptions = userStoreOptions;
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
    }
}
