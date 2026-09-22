using System.Diagnostics;
using FluentValidation;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;
using TodoList.Tasks.Api.ResultMapping;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Application.Security;
using ApplicationCreateTaskRequest = TodoList.Tasks.Application.Tasks.CreateTaskRequest;
using CreateTaskHandler = TodoList.Tasks.Application.Tasks.CreateTaskHandler;
using GetTaskHandler = TodoList.Tasks.Application.Tasks.GetTaskHandler;
using ListTasksHandler = TodoList.Tasks.Application.Tasks.ListTasksHandler;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoGetTaskRequest = TodoList.Contracts.Tasks.V1.GetTaskRequest;
using ProtoListTasksRequest = TodoList.Contracts.Tasks.V1.ListTasksRequest;

namespace TodoList.Tasks.Api.Grpc;

/// <summary>
/// Servidor gRPC do Tasks Service (BE-35) — a única implementação do
/// contrato <c>tasks.v1.TasksService</c> (BE-32), registrada em
/// <c>Program.cs</c> via <c>AddGrpc()</c> + <c>MapGrpcService&lt;&gt;()</c>,
/// nunca por Controller. Substitui o gatilho REST provisório de BE-29
/// (D-30 fechada).
///
/// <para>
/// <b>Ordem de <see cref="CreateTask"/> (contrato desta task, CA-01 a
/// CA-09):</b> (1) <see cref="RequireCallerIdentityInterceptor"/> já garantiu,
/// antes deste método rodar, que <c>x-user-id</c> é um <see cref="Guid"/>
/// válido; (2) <see cref="TaskGrpcMapping.ToApplicationRequest"/> traduz o
/// proto para o request da Application, rejeitando <c>due_date</c> fora do
/// formato como erro de validação, não exceção; (3) o
/// <see cref="IValidator{T}"/> já existente (BE-17) valida título/descrição;
/// (4) <see cref="CreateTaskHandler.HandleAsync"/> roda **sem alteração**
/// (CA-09/CA-15) — a única chamada ao Identity acontece dentro dele; (5) o
/// <see cref="Result{TValue}"/> devolvido vira <see cref="TaskReply"/> ou
/// <see cref="RpcException"/> via <see cref="ResultGrpcStatus"/>.
/// </para>
/// </summary>
public sealed partial class TasksGrpcService : TasksService.TasksServiceBase
{
    private readonly IValidator<ApplicationCreateTaskRequest> _validator;
    private readonly CreateTaskHandler _handler;
    private readonly ListTasksHandler _listTasksHandler;
    private readonly GetTaskHandler _getTaskHandler;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TasksGrpcService> _logger;

    public TasksGrpcService(
        IValidator<ApplicationCreateTaskRequest> validator,
        CreateTaskHandler handler,
        ListTasksHandler listTasksHandler,
        GetTaskHandler getTaskHandler,
        ICurrentUser currentUser,
        ILogger<TasksGrpcService> logger)
    {
        _validator = validator;
        _handler = handler;
        _listTasksHandler = listTasksHandler;
        _getTaskHandler = getTaskHandler;
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async Task<TaskReply> CreateTask(ProtoCreateTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        // CA-16: o traceId propagado do Gateway (W3C traceparent) já virou o
        // pai da Activity corrente pelo host — nenhum código adicional propaga
        // isso, ver a nota técnica de BE-35.
        var traceId = Activity.Current?.Id ?? string.Empty;

        // CA-08/CA-09 já garantido pelo RequireCallerIdentityInterceptor —
        // ICurrentUser.Id não lança aqui.
        var ownerId = _currentUser.Id;

        var mapping = TaskGrpcMapping.ToApplicationRequest(request);

        if (!mapping.IsValid)
        {
            var invalidDueDate = ResultGrpcStatus.ToValidationFailedException(mapping.Errors!);
            Log.CreateTaskCalled(_logger, ownerId, invalidDueDate.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw invalidDueDate;
        }

        var validationResult = await _validator.ValidateAsync(mapping.Request!, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.CreateTaskCalled(_logger, ownerId, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw validationFailed;
        }

        var result = await _handler.HandleAsync(mapping.Request!, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.CreateTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.CreateTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-41, recorte de BE-22 — só tarefas do dono corrente (CA-02), não
    /// removidas (CA-03), ordenadas por criação decrescente (CA-05),
    /// paginadas no banco (CA-11). <c>page</c>/<c>page_size</c> fora dos
    /// limites viram <see cref="StatusCode.InvalidArgument"/> pelo próprio
    /// <see cref="ListTasksHandler.HandleAsync"/> (CA-07), traduzido aqui
    /// pelo mesmo <see cref="ResultGrpcStatus"/> de <see cref="CreateTask"/> —
    /// sem validação duplicada neste método.
    /// </summary>
    public override async Task<ListTasksReply> ListTasks(ProtoListTasksRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;
        var ownerId = _currentUser.Id;

        var applicationRequest = TaskGrpcMapping.ToApplicationRequest(request);
        var result = await _listTasksHandler.HandleAsync(applicationRequest, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.ListTasksCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.ListTasksCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return TaskGrpcMapping.ToListTasksReply(result.Value);
    }

    /// <summary>
    /// BE-41, recorte de BE-18 — <c>id</c> em formato inválido é
    /// <see cref="StatusCode.InvalidArgument"/> (nunca <c>NotFound</c>: são
    /// causas diferentes, mesma distinção de <c>due_date</c> em
    /// <see cref="CreateTask"/>); tarefa inexistente, de outro dono ou
    /// removida são o mesmo <see cref="StatusCode.NotFound"/>
    /// (RN-AUTZ-03, CA-13 a CA-16) — a indistinguibilidade vem inteira de
    /// <see cref="GetTaskHandler"/>, este método não adiciona nem remove
    /// nenhum detalhe.
    /// </summary>
    public override async Task<TaskReply> GetTask(ProtoGetTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var traceId = Activity.Current?.Id ?? string.Empty;
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.GetTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw invalidId;
        }

        var result = await _getTaskHandler.HandleAsync(taskId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.GetTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds, traceId);

            throw failure;
        }

        Log.GetTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds, traceId);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    private static partial class Log
    {
        // Mesmo padrão de GrpcIdentityGateway.Log: ownerId, statusCode,
        // durationMs e traceId — nunca título/descrição da tarefa (dado de
        // negócio do usuário, não identificador técnico).
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "CreateTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void CreateTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ListTasks: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void ListTasksCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs, string traceId);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "GetTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void GetTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs, string traceId);
    }
}
