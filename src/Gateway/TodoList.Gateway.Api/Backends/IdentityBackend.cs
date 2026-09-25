using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Implementação de <see cref="IIdentityBackend"/> sobre o cliente gRPC
/// gerado a partir de <c>identity.proto</c> (BE-36) — único ponto do Gateway
/// que conhece <c>TodoList.Contracts.Identity.V1</c> (CA-04). Nunca deixa uma
/// <see cref="RpcException"/> escapar: indisponibilidade vira
/// <see cref="BackendUnavailableException"/> (D-28, CA-24); qualquer outro
/// status inesperado vira <see cref="BackendCallException"/>, para que
/// <see cref="ErrorHandling.GrpcErrorMapping"/> decida o HTTP. Desde
/// BE-40/D-38, a autenticação de entrada é local (<c>AddJwtBearer</c>) —
/// <c>ValidateToken</c> não tem consumidor aqui. Onda 2 da Fase 3 acrescenta
/// <c>Register</c>/<c>GetProfile</c>/<c>UpdateProfile</c>/<c>ChangePassword</c>/
/// <c>DeleteAccount</c>, todos com o mesmo padrão de deadline/log/tradução de
/// erro de <see cref="LoginAsync"/>.
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

    /// <summary>Chama <c>Register</c> (BE-07) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> RegisterAsync(string? email, string? password, string? displayName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await _client.RegisterAsync(
                new RegisterRequest { Email = email ?? string.Empty, Password = password ?? string.Empty, DisplayName = displayName ?? string.Empty },
                callOptions);

            Log.CallSucceeded(_logger, BackendName, "Register", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "Register", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>Chama <c>GetProfile</c> (BE-14) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await _client.GetProfileAsync(new GetProfileRequest { UserId = userId }, callOptions);

            Log.CallSucceeded(_logger, BackendName, "GetProfile", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "GetProfile", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>Chama <c>UpdateProfile</c> (BE-14, RN-USER-02) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> UpdateProfileAsync(string userId, string? displayName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await _client.UpdateProfileAsync(
                new UpdateProfileRequest { UserId = userId, DisplayName = displayName ?? string.Empty },
                callOptions);

            Log.CallSucceeded(_logger, BackendName, "UpdateProfile", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "UpdateProfile", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>
    /// Chama <c>ChangePassword</c> (BE-15) — mesmo padrão de deadline/log/tradução
    /// de erro de <see cref="LoginAsync"/>; a resposta é <c>google.protobuf.Empty</c>,
    /// então não há nada a traduzir de volta.
    /// </summary>
    public async Task ChangePasswordAsync(string userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            await _client.ChangePasswordAsync(
                new ChangePasswordRequest { UserId = userId, CurrentPassword = currentPassword ?? string.Empty, NewPassword = newPassword ?? string.Empty },
                callOptions);

            Log.CallSucceeded(_logger, BackendName, "ChangePassword", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "ChangePassword", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>
    /// Chama <c>DeleteAccount</c> (BE-16, D-19) — mesmo padrão de deadline/log/tradução
    /// de erro de <see cref="LoginAsync"/>; a resposta é <c>google.protobuf.Empty</c>.
    /// </summary>
    public async Task DeleteAccountAsync(string userId, string? password, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            await _client.DeleteAccountAsync(
                new DeleteAccountRequest { UserId = userId, Password = password ?? string.Empty },
                callOptions);

            Log.CallSucceeded(_logger, BackendName, "DeleteAccount", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "DeleteAccount", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ToBackendException(ex);
        }
    }

    /// <summary>
    /// Tradução comum de <c>RegisterResponse</c>/<c>ProfileResponse</c> (mesmo
    /// formato de campos) para o DTO HTTP — nunca contém senha/hash (RN-AUTH-05).
    /// </summary>
    private static ProfileHttpResponse ToProfileHttpResponse(string id, string email, string displayName, Timestamp createdAt) =>
        new(id, email, displayName, createdAt.ToDateTimeOffset());

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
