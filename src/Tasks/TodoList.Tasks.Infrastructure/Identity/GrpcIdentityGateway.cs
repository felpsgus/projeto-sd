using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Identity.V1;
using TodoList.Tasks.Application.Identity;

namespace TodoList.Tasks.Infrastructure.Identity;

/// <summary>
/// Implementação de <see cref="IIdentityGateway"/> sobre o cliente gRPC
/// gerado a partir do <c>.proto</c> (BE-27, CA-01). Único ponto do Tasks
/// Service que conhece <c>TodoList.Contracts.Identity.V1</c> — a resposta é
/// traduzida para <see cref="UserValidation"/> antes de sair daqui, e uma
/// <see cref="RpcException"/> nunca escapa desta classe (CA-08).
/// </summary>
public sealed partial class GrpcIdentityGateway : IIdentityGateway
{
    private readonly IdentityService.IdentityServiceClient _client;
    private readonly IdentityGrpcOptions _options;
    private readonly ILogger<GrpcIdentityGateway> _logger;

    public GrpcIdentityGateway(
        IdentityService.IdentityServiceClient client,
        IOptions<IdentityGrpcOptions> options,
        ILogger<GrpcIdentityGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UserValidation> ValidateUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Capturado uma vez: é o valor propagado na metadata (o cliente gRPC abre a própria Activity em volta da chamada).
        var traceId = Activity.Current?.Id ?? string.Empty;

        var callOptions = new CallOptions(
            headers: BuildMetadata(traceId),
            deadline: DateTime.UtcNow.AddSeconds(_options.GrpcTimeoutSeconds),
            cancellationToken: cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await _client.ValidateUserAsync(
                new ValidateUserRequest { UserId = userId.ToString() },
                callOptions);

            Log.ValidateUserCalled(_logger, userId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

            return new UserValidation(response.Exists, response.Active, response.DisplayName);
        }
        catch (RpcException ex)
        {
            Log.ValidateUserFailed(_logger, userId, ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            // BE-03 (Result<T>/Error) ainda não existe nesta base de código.
            // A intenção da especificação — traduzir a RpcException no Error
            // "identity.unavailable" do catálogo do Tasks — fica registrada
            // nesta exceção própria da Application (ver o comentário em
            // IdentityUnavailableException). O requisito duro desta etapa
            // (CA-08) é que a RpcException não atravesse a Infrastructure, e
            // aqui ela é capturada e traduzida antes de sair.
            throw new IdentityUnavailableException(
                $"Identity indisponível ao validar o usuário {userId} (status gRPC: {ex.StatusCode}).", ex);
        }
    }

    /// <summary>
    /// Propaga o <c>traceId</c> da requisição HTTP em andamento para o
    /// Identity via metadata gRPC (BE-27, CA-13; BE-24). O valor é o rastro
    /// criado pelo ASP.NET Core para a requisição (<see cref="Activity.Current"/>) —
    /// nenhuma infraestrutura de tracing adicional é introduzida aqui.
    /// </summary>
    private static Metadata BuildMetadata(string traceId)
    {
        var metadata = new Metadata();

        if (!string.IsNullOrEmpty(traceId))
        {
            metadata.Add("traceparent", traceId);
        }

        return metadata;
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ValidateUser (Identity gRPC): userId={UserId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void ValidateUserCalled(ILogger logger, Guid userId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "ValidateUser (Identity gRPC) falhou: userId={UserId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void ValidateUserFailed(ILogger logger, Guid userId, StatusCode statusCode, double durationMs);
    }
}
