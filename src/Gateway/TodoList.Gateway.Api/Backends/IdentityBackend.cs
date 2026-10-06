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
/// <see cref="ErrorHandling.GrpcErrorMapping"/> decida o HTTP. A
/// autenticação de entrada é local (<c>AddJwtBearer</c>, chave pública RSA),
/// sem chamada ao Identity por requisição. Onda 2 da Fase 3 acrescenta
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
        var response = await CallAsync(
            "Login",
            options => _client.LoginAsync(new LoginRequest { Email = email, Password = password }, options),
            cancellationToken);

        // response.ExpiresAt só vem preenchido quando succeeded=true (contrato de LoginResponse) —
        // acessá-lo sem essa checagem lançaria NullReferenceException no caminho de credencial inválida.
        if (!response.Succeeded)
        {
            return new LoginOutcome(
                false, string.Empty, default, RetryAfterSeconds: response.LockedOut ? Math.Max(1, response.RetryAfterSeconds) : null);
        }

        return new LoginOutcome(
            true,
            response.AccessToken,
            response.ExpiresAt.ToDateTimeOffset(),
            response.RefreshToken,
            response.RefreshTokenExpiresAt?.ToDateTimeOffset() ?? default);
    }

    /// <summary>Chama <c>RefreshSession</c> (BE-10) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>; o token nunca aparece no log.</summary>
    public async Task<RefreshOutcome> RefreshSessionAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "RefreshSession",
            options => _client.RefreshSessionAsync(new RefreshSessionRequest { RefreshToken = refreshToken }, options),
            cancellationToken);

        if (!response.Succeeded)
        {
            return new RefreshOutcome(false, string.Empty, default, string.Empty, default, response.Revoked);
        }

        return new RefreshOutcome(
            true,
            response.AccessToken,
            response.ExpiresAt.ToDateTimeOffset(),
            response.RefreshToken,
            response.RefreshTokenExpiresAt.ToDateTimeOffset());
    }

    /// <summary>Chama <c>Logout</c> (BE-11); a resposta é <c>google.protobuf.Empty</c>.</summary>
    public async Task LogoutAsync(string userId, string refreshToken, CancellationToken cancellationToken) =>
        await CallAsync(
            "Logout",
            options => _client.LogoutAsync(new LogoutRequest { UserId = userId, RefreshToken = refreshToken }, options),
            cancellationToken);

    /// <summary>Chama <c>LogoutAll</c> (BE-11, RN-AUTH-19); a resposta é <c>google.protobuf.Empty</c>.</summary>
    public async Task LogoutAllAsync(string userId, CancellationToken cancellationToken) =>
        await CallAsync(
            "LogoutAll",
            options => _client.LogoutAllAsync(new LogoutAllRequest { UserId = userId }, options),
            cancellationToken);

    /// <summary>Chama <c>Register</c> (BE-07) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> RegisterAsync(string? email, string? password, string? displayName, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "Register",
            options => _client.RegisterAsync(
                new RegisterRequest { Email = email ?? string.Empty, Password = password ?? string.Empty, DisplayName = displayName ?? string.Empty },
                options),
            cancellationToken);

        return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
    }

    /// <summary>Chama <c>GetProfile</c> (BE-14) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "GetProfile",
            options => _client.GetProfileAsync(new GetProfileRequest { UserId = userId }, options),
            cancellationToken);

        return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
    }

    /// <summary>Chama <c>UpdateProfile</c> (BE-14, RN-USER-02) — mesmo padrão de deadline/log/tradução de erro de <see cref="LoginAsync"/>.</summary>
    public async Task<ProfileHttpResponse> UpdateProfileAsync(string userId, string? displayName, CancellationToken cancellationToken)
    {
        var response = await CallAsync(
            "UpdateProfile",
            options => _client.UpdateProfileAsync(
                new UpdateProfileRequest { UserId = userId, DisplayName = displayName ?? string.Empty },
                options),
            cancellationToken);

        return ToProfileHttpResponse(response.Id, response.Email, response.DisplayName, response.CreatedAt);
    }

    /// <summary>
    /// Chama <c>ChangePassword</c> (BE-15) — mesmo padrão de deadline/log/tradução
    /// de erro de <see cref="LoginAsync"/>; a resposta é <c>google.protobuf.Empty</c>,
    /// então não há nada a traduzir de volta.
    /// </summary>
    public async Task ChangePasswordAsync(string userId, string? currentPassword, string? newPassword, CancellationToken cancellationToken) =>
        await CallAsync(
            "ChangePassword",
            options => _client.ChangePasswordAsync(
                new ChangePasswordRequest { UserId = userId, CurrentPassword = currentPassword ?? string.Empty, NewPassword = newPassword ?? string.Empty },
                options),
            cancellationToken);

    /// <summary>
    /// Chama <c>DeleteAccount</c> (BE-16, D-19) — mesmo padrão de deadline/log/tradução
    /// de erro de <see cref="LoginAsync"/>; a resposta é <c>google.protobuf.Empty</c>.
    /// </summary>
    public async Task DeleteAccountAsync(string userId, string? password, CancellationToken cancellationToken) =>
        await CallAsync(
            "DeleteAccount",
            options => _client.DeleteAccountAsync(
                new DeleteAccountRequest { UserId = userId, Password = password ?? string.Empty },
                options),
            cancellationToken);

    /// <summary>
    /// Padrão comum de toda chamada de saída: deadline (<c>IdentityGrpcTimeoutSeconds</c>),
    /// <c>traceparent</c>, log de sucesso/falha com duração e tradução de
    /// <see cref="RpcException"/> em <see cref="ToBackendException"/>.
    /// </summary>
    private async Task<TResponse> CallAsync<TResponse>(
        string rpc, Func<CallOptions, AsyncUnaryCall<TResponse>> call, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var callOptions = new CallOptions(
                headers: BuildTraceparentHeaders(traceId),
                deadline: DateTime.UtcNow.AddSeconds(_options.IdentityGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            var response = await call(callOptions);

            Log.CallSucceeded(_logger, BackendName, rpc, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, rpc, ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

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
        // BE-36, CA-26: backend, rpc, statusCode e durationMs na
        // própria mensagem — nunca o token/senha (nem os parâmetros de
        // entrada aparecem no template).
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Chamada gRPC de saída: backend={Backend}, rpc={Rpc}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void CallSucceeded(ILogger logger, string backend, string rpc, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Chamada gRPC de saída falhou: backend={Backend}, rpc={Rpc}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void CallFailed(ILogger logger, string backend, string rpc, StatusCode statusCode, double durationMs);
    }
}
