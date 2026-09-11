using System.Diagnostics;
using FluentValidation;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;
using TodoList.Tasks.Api.ResultMapping;
using TodoList.Tasks.Api.Security;
using TodoList.Tasks.Application.Security;
using ApplicationCreateTaskRequest = TodoList.Tasks.Application.Tasks.CreateTaskRequest;
using CreateTaskHandler = TodoList.Tasks.Application.Tasks.CreateTaskHandler;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;

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
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<TasksGrpcService> _logger;

    public TasksGrpcService(
        IValidator<ApplicationCreateTaskRequest> validator,
        CreateTaskHandler handler,
        ICurrentUser currentUser,
        ILogger<TasksGrpcService> logger)
    {
        _validator = validator;
        _handler = handler;
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

    private static partial class Log
    {
        // Mesmo padrão de GrpcIdentityGateway.Log: ownerId, statusCode,
        // durationMs e traceId — nunca título/descrição da tarefa (dado de
        // negócio do usuário, não identificador técnico).
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "CreateTask: ownerId={OwnerId}, statusCode={StatusCode}, durationMs={DurationMs}, traceId={TraceId}")]
        public static partial void CreateTaskCalled(ILogger logger, Guid ownerId, StatusCode statusCode, double durationMs, string traceId);
    }
}
