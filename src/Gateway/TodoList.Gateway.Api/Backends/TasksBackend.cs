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

    /// <summary>Chama <c>ListTasks</c> (BE-41, CA-17) — mesmo padrão de deadline/log/tradução de erro de <see cref="CreateTaskAsync"/>.</summary>
    public async Task<ListTasksHttpResponse> ListTasksAsync(ListTasksHttpRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.ListTasksAsync(
                TaskTranslation.ToProtoRequest(request),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "ListTasks", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "ListTasks", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    /// <summary>Chama <c>GetTask</c> (BE-41, CA-20/CA-21) — mesmo padrão de deadline/log/tradução de erro de <see cref="CreateTaskAsync"/>.</summary>
    public async Task<TaskHttpResponse> GetTaskAsync(string id, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.GetTaskAsync(
                TaskTranslation.ToProtoGetTaskRequest(id),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "GetTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "GetTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    /// <summary>Chama <c>UpdateTask</c> (BE-19) — mesmo padrão de deadline/log/tradução de erro de <see cref="CreateTaskAsync"/>.</summary>
    public async Task<TaskHttpResponse> UpdateTaskAsync(string id, UpdateTaskHttpRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.UpdateTaskAsync(
                TaskTranslation.ToProtoRequest(id, request),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "UpdateTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "UpdateTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    /// <summary>Chama <c>CompleteTask</c> (BE-20) — mesmo padrão de deadline/log/tradução de erro de <see cref="CreateTaskAsync"/>.</summary>
    public async Task<TaskHttpResponse> CompleteTaskAsync(string id, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.CompleteTaskAsync(
                TaskTranslation.ToProtoCompleteTaskRequest(id),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "CompleteTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "CompleteTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    /// <summary>Chama <c>ReopenTask</c> (BE-20) — mesmo padrão de deadline/log/tradução de erro de <see cref="CreateTaskAsync"/>.</summary>
    public async Task<TaskHttpResponse> ReopenTaskAsync(string id, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            var reply = await _client.ReopenTaskAsync(
                TaskTranslation.ToProtoReopenTaskRequest(id),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "ReopenTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

            return TaskTranslation.ToHttpResponse(reply);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "ReopenTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded
                ? new BackendUnavailableException(BackendName, ex)
                : new BackendCallException(ex);
        }
    }

    /// <summary>
    /// Chama <c>DeleteTask</c> (BE-21) — mesmo padrão de deadline/log/tradução
    /// de erro de <see cref="CreateTaskAsync"/>; a resposta é
    /// <c>google.protobuf.Empty</c>, então não há nada a traduzir de volta.
    /// </summary>
    public async Task DeleteTaskAsync(string id, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;

        try
        {
            await _client.DeleteTaskAsync(
                TaskTranslation.ToProtoDeleteTaskRequest(id),
                deadline: DateTime.UtcNow.AddSeconds(_options.TasksGrpcTimeoutSeconds),
                cancellationToken: cancellationToken);

            Log.CallSucceeded(_logger, BackendName, "DeleteTask", StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);
        }
        catch (RpcException ex)
        {
            Log.CallFailed(_logger, BackendName, "DeleteTask", ex.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

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
