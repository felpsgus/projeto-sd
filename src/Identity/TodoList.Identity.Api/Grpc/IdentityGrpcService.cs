using System.Diagnostics;
using Grpc.Core;
using TodoList.Contracts.Identity.V1;
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
    private readonly ILogger<IdentityGrpcService> _logger;

    public IdentityGrpcService(IUserLookup userLookup, ILogger<IdentityGrpcService> logger)
    {
        _userLookup = userLookup;
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
    /// Stub intencional (D-31, CA-11/CA-12 de BE-26): sempre <c>valid=false</c>,
    /// sem nenhuma lógica de validação de JWT. Este é o único caminho para
    /// validar um token fora do Identity — a chave de assinatura HS256 não sai
    /// daqui. A implementação real (etapa do API Gateway) reutilizará o
    /// validador de BE-08 sem expor a chave.
    /// </summary>
    public override Task<ValidateTokenResponse> ValidateToken(ValidateTokenRequest request, ServerCallContext context) =>
        Task.FromResult(new ValidateTokenResponse { Valid = false, UserId = string.Empty });

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

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ValidateUser: userId={UserId}, exists={Exists}, active={Active}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void ValidateUserCalled(ILogger logger, string userId, bool exists, bool active, double durationMs, string traceId);
    }
}
