using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Implementação de <see cref="IIdentityBackend"/> sobre o cliente gRPC
/// gerado a partir de <c>identity.proto</c> (BE-36) — único ponto do Gateway
/// que conhece <c>TodoList.Contracts.Identity.V1</c> (CA-04). Nunca deixa uma
/// <see cref="RpcException"/> escapar: indisponibilidade vira
/// <see cref="BackendUnavailableException"/> (D-28, CA-13/CA-24); qualquer
/// outro status inesperado vira <see cref="BackendCallException"/>, para que
/// <see cref="ErrorHandling.GrpcErrorMapping"/> decida o HTTP.
/// </summary>
public sealed partial class IdentityBackend : IIdentityBackend
{
    private const string BackendName = "Identity";

    private readonly IdentityService.IdentityServiceClient _client;
    private readonly BackendOptions _options;
    private readonly ILogger<IdentityBackend> _logger;

    public IdentityBackend(
        IdentityService.IdentityServiceClient client,
        IOptions<BackendOptions> options,
        ILogger<IdentityBackend> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TokenValidation> ValidateTokenAsync(string accessToken, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await _client.ValidateTokenAsync(new ValidateTokenRequest { AccessToken = accessToken }, callOptions);

            Log.CallSucceeded(_logger, BackendName, "ValidateToken", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return new TokenValidation(response.Valid, response.UserId);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "ValidateToken", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    public async Task<LoginOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await _client.LoginAsync(new LoginRequest { Email = email, Password = password }, callOptions);

            Log.CallSucceeded(_logger, BackendName, "Login", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            // response.ExpiresAt só vem preenchido quando succeeded=true (contrato de LoginResponse) —
            // acessá-lo sem essa checagem lançaria NullReferenceException no caminho de credencial inválida.
            var expiresAt = response.Succeeded ? response.ExpiresAt.ToDateTimeOffset() : default;

            return new LoginOutcome(response.Succeeded, response.AccessToken, expiresAt);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "Login", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>
    /// Propaga o <c>traceparent</c> W3C corrente na metadata gRPC de saída
    /// (CA-25) — explícito, e não deixado só para a propagação automática do
    /// <see cref="HttpClient"/> (que depende do handler HTTP concreto por
    /// trás do canal e não é garantida, ex.: em testes com um handler em
    /// memória) — mesmo padrão de <c>GrpcIdentityGateway.BuildMetadata</c> no
    /// Tasks Service.
    /// </summary>
    private static Metadata BuildTraceparentHeaders(string traceId)
    {
        var metadata = new Metadata();

        if (!string.IsNullOrEmpty(traceId))
        {
            metadata.Add("traceparent", traceId);
        }

        return metadata;
    }

    private static Exception ToBackendException(RpcException ex) =>
        ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
            ? new BackendUnavailableException(BackendName, ex)
            : new BackendCallException(ex);

    private static partial class Log
    {
        // BE-36, CA-26: backend, rpc, statusCode, durationMs e traceId na
        // própria mensagem — nunca o token/senha (nem os parâmetros de
        // entrada aparecem no template).
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Chamada gRPC de saída: backend={Backend}, rpc={Rpc}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void CallSucceeded(ILogger logger, string backend, string rpc, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Chamada gRPC de saída falhou: backend={Backend}, rpc={Rpc}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void CallFailed(ILogger logger, string backend, string rpc, StatusCode statusCode, double durationMs, string traceId);
    }
}
