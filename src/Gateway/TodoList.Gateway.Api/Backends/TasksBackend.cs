using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Options;
using TodoList.Contracts.Tasks.V1;
using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Implementação de <see cref="ITasksBackend"/> sobre o cliente gRPC gerado a
/// partir de <c>tasks.proto</c> (BE-36) — único ponto do Gateway que conhece
/// <c>TodoList.Contracts.Tasks.V1</c> (CA-04), além de <see cref="TaskTranslation"/>.
/// A metadata <c>x-user-id</c>/<c>x-client-date</c> é acrescentada pelo
/// <see cref="ClientMetadataInterceptor"/> registrado neste cliente — esta
/// classe só cuida de deadline, tradução e log.
/// </summary>
public sealed partial class TasksBackend : ITasksBackend
{
    private const string BackendName = "Tasks";

    private readonly TasksService.TasksServiceClient _client;
    private readonly BackendOptions _options;
    private readonly ILogger<TasksBackend> _logger;

    public TasksBackend(
        TasksService.TasksServiceClient client,
        IOptions<BackendOptions> options,
        ILogger<TasksBackend> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TaskHttpResponse> CreateTaskAsync(CreateTaskHttpRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.CreateTaskAsync(
                TaskTranslation.ToProtoRequest(request),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "CreateTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "CreateTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    private static partial class Log
    {
        // Mesmo padrão de IdentityBackend.Log (BE-36, CA-26) — nunca o
        // título/descrição da tarefa (dado de negócio do usuário).
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
