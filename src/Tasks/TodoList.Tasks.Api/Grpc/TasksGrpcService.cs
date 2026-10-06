using System.Diagnostics;
using FluentValidation;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;
using TodoList.Tasks.Api.ResultMapping;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Application.Security;
using ApplicationCreateTaskRequest = TodoList.Tasks.Application.Tasks.CreateTaskRequest;
using CompleteTaskHandler = TodoList.Tasks.Application.Tasks.CompleteTaskHandler;
using CreateTaskHandler = TodoList.Tasks.Application.Tasks.CreateTaskHandler;
using DeleteTaskHandler = TodoList.Tasks.Application.Tasks.DeleteTaskHandler;
using GetTaskHandler = TodoList.Tasks.Application.Tasks.GetTaskHandler;
using ListTasksHandler = TodoList.Tasks.Application.Tasks.ListTasksHandler;
using ProtoCompleteTaskRequest = TodoList.Contracts.Tasks.V1.CompleteTaskRequest;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoDeleteTaskRequest = TodoList.Contracts.Tasks.V1.DeleteTaskRequest;
using ProtoGetTaskRequest = TodoList.Contracts.Tasks.V1.GetTaskRequest;
using ProtoListTasksRequest = TodoList.Contracts.Tasks.V1.ListTasksRequest;
using ProtoReopenTaskRequest = TodoList.Contracts.Tasks.V1.ReopenTaskRequest;
using ProtoUpdateTaskRequest = TodoList.Contracts.Tasks.V1.UpdateTaskRequest;
using ReopenTaskHandler = TodoList.Tasks.Application.Tasks.ReopenTaskHandler;
using UpdateTaskHandler = TodoList.Tasks.Application.Tasks.UpdateTaskHandler;

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
    private readonly UpdateTaskHandler _updateTaskHandler;
    private readonly CompleteTaskHandler _completeTaskHandler;
    private readonly ReopenTaskHandler _reopenTaskHandler;
    private readonly DeleteTaskHandler _deleteTaskHandler;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TasksGrpcService> _logger;

    public TasksGrpcService(
        IValidator<ApplicationCreateTaskRequest> validator,
        CreateTaskHandler handler,
        ListTasksHandler listTasksHandler,
        GetTaskHandler getTaskHandler,
        UpdateTaskHandler updateTaskHandler,
        CompleteTaskHandler completeTaskHandler,
        ReopenTaskHandler reopenTaskHandler,
        DeleteTaskHandler deleteTaskHandler,
        ICurrentUser currentUser,
        ILogger<TasksGrpcService> logger)
    {
        _validator = validator;
        _handler = handler;
        _listTasksHandler = listTasksHandler;
        _getTaskHandler = getTaskHandler;
        _updateTaskHandler = updateTaskHandler;
        _completeTaskHandler = completeTaskHandler;
        _reopenTaskHandler = reopenTaskHandler;
        _deleteTaskHandler = deleteTaskHandler;
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async Task<TaskReply> CreateTask(ProtoCreateTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        // CA-16: o traceId propagado do Gateway (W3C traceparent) já virou o
        // pai da Activity corrente pelo host — nenhum código adicional propaga
        // isso, ver a nota técnica de BE-35.

        // CA-08/CA-09 já garantido pelo RequireCallerIdentityInterceptor —
        // ICurrentUser.Id não lança aqui.
        var ownerId = _currentUser.Id;

        var mapping = TaskGrpcMapping.ToApplicationRequest(request);

        if (!mapping.IsValid)
        {
            var invalidDueDate = ResultGrpcStatus.ToValidationFailedException(mapping.Errors!);
            Log.CreateTaskCalled(_logger, ownerId, invalidDueDate.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidDueDate;
        }

        var validationResult = await _validator.ValidateAsync(mapping.Request!, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.CreateTaskCalled(_logger, ownerId, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw validationFailed;
        }

        var result = await _handler.HandleAsync(mapping.Request!, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.CreateTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.CreateTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-22 — só tarefas do dono corrente (CA-01), não removidas (CA-02),
    /// filtráveis por estado/prioridade/atraso, buscáveis por título e
    /// descrição, ordenadas pelo critério fixo de RN-LIST-06, paginadas no
    /// banco (CA-32). <c>status</c>/<c>priority</c> com valor fora do enum
    /// viram <see cref="StatusCode.InvalidArgument"/> aqui, pelo mapeamento
    /// (<see cref="TaskGrpcMapping.ToApplicationRequest(ProtoListTasksRequest)"/>,
    /// CA-11); <c>page</c>/<c>page_size</c> fora dos limites viram o mesmo
    /// status pelo <see cref="ListTasksHandler.HandleAsync"/> (CA-29/CA-30),
    /// os dois traduzidos pelo mesmo <see cref="ResultGrpcStatus"/> de
    /// <see cref="CreateTask"/>.
    /// </summary>
    public override async Task<ListTasksReply> ListTasks(ProtoListTasksRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerId = _currentUser.Id;

        var mapping = TaskGrpcMapping.ToApplicationRequest(request);

        if (!mapping.IsValid)
        {
            var invalidFilter = ResultGrpcStatus.ToValidationFailedException(mapping.Errors!);
            Log.ListTasksCalled(_logger, ownerId, invalidFilter.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidFilter;
        }

        var result = await _listTasksHandler.HandleAsync(mapping.Request!, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.ListTasksCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.ListTasksCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

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
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.GetTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidId;
        }

        var result = await _getTaskHandler.HandleAsync(taskId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.GetTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.GetTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-19 — substituição completa dos campos editáveis. Reaproveita, sem
    /// duplicar, exatamente o mesmo <see cref="IValidator{T}"/> de
    /// <see cref="CreateTask"/> (CA-15 de BE-19): o mapeamento já resolveu
    /// <c>due_date</c>/<c>priority</c> com a mesma lógica de <c>CreateTask</c>
    /// (<see cref="TaskGrpcMapping.ToApplicationRequest(ProtoUpdateTaskRequest, Guid)"/>),
    /// então validar título/descrição é rodar o validador de BE-17 sobre um
    /// <see cref="ApplicationCreateTaskRequest"/> efêmero com os mesmos
    /// valores — nenhuma regra nova, nenhuma classe de validador nova.
    /// </summary>
    public override async Task<TaskReply> UpdateTask(ProtoUpdateTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.UpdateTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidId;
        }

        var mapping = TaskGrpcMapping.ToApplicationRequest(request, taskId);

        if (!mapping.IsValid)
        {
            var invalidDueDate = ResultGrpcStatus.ToValidationFailedException(mapping.Errors!);
            Log.UpdateTaskCalled(_logger, ownerId, invalidDueDate.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidDueDate;
        }

        // CA-15 de BE-19: mesmo IValidator<ApplicationCreateTaskRequest> de
        // CreateTask, sobre um request efêmero com os mesmos campos — sem
        // validador próprio para UpdateTask.
        var fieldsToValidate = new ApplicationCreateTaskRequest(
            mapping.Request!.Title, mapping.Request.Description, mapping.Request.Priority, mapping.Request.DueDate);
        var validationResult = await _validator.ValidateAsync(fieldsToValidate, context.CancellationToken);

        if (!validationResult.IsValid)
        {
            var validationFailed = validationResult.ToValidationFailedException();
            Log.UpdateTaskCalled(_logger, ownerId, validationFailed.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw validationFailed;
        }

        var result = await _updateTaskHandler.HandleAsync(mapping.Request!, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.UpdateTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.UpdateTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-20 — conclui uma tarefa Pending própria (RN-TASK-08). A transição
    /// inválida (já concluída) vira <see cref="StatusCode.FailedPrecondition"/>
    /// (D-35) através de <see cref="ResultGrpcStatus"/>, sem tratamento
    /// especial neste método — o mesmo caminho de qualquer outra falha de
    /// <see cref="Result{TValue}"/>.
    /// </summary>
    public override async Task<TaskReply> CompleteTask(ProtoCompleteTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.CompleteTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidId;
        }

        var result = await _completeTaskHandler.HandleAsync(taskId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.CompleteTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.CompleteTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-20 — reabre uma tarefa Completed própria (RN-TASK-09). Mesmo
    /// desenho de <see cref="CompleteTask"/>, espelhado.
    /// </summary>
    public override async Task<TaskReply> ReopenTask(ProtoReopenTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.ReopenTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidId;
        }

        var result = await _reopenTaskHandler.HandleAsync(taskId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.ReopenTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.ReopenTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return TaskGrpcMapping.ToTaskReply(result.Value);
    }

    /// <summary>
    /// BE-21 — remove (soft delete) uma tarefa própria (RN-TASK-12/13).
    /// Devolve <see cref="Empty"/>: a borda REST (Gateway) traduz isso em
    /// <c>204 No Content</c>, e não há nenhum dado da tarefa que valha a pena
    /// devolver depois de removê-la (ver a nota técnica do RPC em
    /// <c>tasks.proto</c>).
    /// </summary>
    public override async Task<Empty> DeleteTask(ProtoDeleteTaskRequest request, ServerCallContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var ownerId = _currentUser.Id;

        if (!TaskGrpcMapping.TryParseTaskId(request, out var taskId))
        {
            var errors = new Dictionary<string, string[]>
            {
                [TaskGrpcMapping.TaskIdFieldName] = ["O id da tarefa deve ser um Guid válido."],
            };
            var invalidId = ResultGrpcStatus.ToValidationFailedException(errors);
            Log.DeleteTaskCalled(_logger, ownerId, invalidId.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw invalidId;
        }

        var result = await _deleteTaskHandler.HandleAsync(taskId, context.CancellationToken);

        if (result.IsFailure)
        {
            var failure = result.ToRpcException();
            Log.DeleteTaskCalled(_logger, ownerId, failure.StatusCode, stopwatch.Elapsed.TotalMilliseconds);

            throw failure;
        }

        Log.DeleteTaskCalled(_logger, ownerId, StatusCode.OK, stopwatch.Elapsed.TotalMilliseconds);

        return new Empty();
    }

    private static partial class Log
    {
        // Mesmo padrão de GrpcIdentityGateway.Log: ownerId, statusCode,
        // durationMs — nunca título/descrição da tarefa (dado de
        // negócio do usuário, não identificador técnico).
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "CreateTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void CreateTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ListTasks: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void ListTasksCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "GetTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void GetTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "UpdateTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void UpdateTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "CompleteTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void CompleteTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "ReopenTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void ReopenTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "DeleteTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}")]
        public static partial void DeleteTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs);
    }
}
