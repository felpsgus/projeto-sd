using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using TodoList.Tasks.Domain.Tasks;
using ApplicationCreateTaskRequest = TodoList.Tasks.Application.Tasks.CreateTaskRequest;
using ApplicationListTasksRequest = TodoList.Tasks.Application.Tasks.ListTasksRequest;
using ApplicationListTasksResponse = TodoList.Tasks.Application.Tasks.ListTasksResponse;
using ApplicationTaskResponse = TodoList.Tasks.Application.Tasks.TaskResponse;
using ApplicationTaskStatusFilter = TodoList.Tasks.Application.Tasks.TaskStatusFilter;
using ApplicationUpdateTaskRequest = TodoList.Tasks.Application.Tasks.UpdateTaskRequest;
using ProtoCompleteTaskRequest = TodoList.Contracts.Tasks.V1.CompleteTaskRequest;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoDeleteTaskRequest = TodoList.Contracts.Tasks.V1.DeleteTaskRequest;
using ProtoGetTaskRequest = TodoList.Contracts.Tasks.V1.GetTaskRequest;
using ProtoListTasksReply = TodoList.Contracts.Tasks.V1.ListTasksReply;
using ProtoListTasksRequest = TodoList.Contracts.Tasks.V1.ListTasksRequest;
using ProtoReopenTaskRequest = TodoList.Contracts.Tasks.V1.ReopenTaskRequest;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;
using ProtoTaskStatusFilter = TodoList.Contracts.Tasks.V1.TaskStatusFilter;
using ProtoUpdateTaskRequest = TodoList.Contracts.Tasks.V1.UpdateTaskRequest;

namespace TodoList.Tasks.Api.Grpc;

/// <summary>
/// Tradução explícita proto ↔ Application do RPC <c>CreateTask</c> (BE-35) —
/// isolada nesta classe estática, testável sem subir servidor gRPC nenhum,
/// para que nenhum tipo gerado pelo <c>.proto</c> vaze para dentro de
/// <see cref="TasksGrpcService"/> além do ponto de entrada/saída do RPC.
/// </summary>
public static class TaskGrpcMapping
{
    /// <summary>
    /// Nome do campo usado no dicionário de erros (BE-35, CA-03) quando
    /// <c>due_date</c> não está no formato <c>yyyy-MM-dd</c> — mesmo nome de
    /// propriedade de <see cref="ApplicationCreateTaskRequest.DueDate"/>, para
    /// que o Gateway reconstrua o mesmo dicionário que o <c>ValidationProblem</c>
    /// REST devolvia (<c>Gateway/Validation/ValidationFilter.cs</c> serializa as
    /// chaves de <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>
    /// sem nenhuma política de <i>camelCase</i> aplicada às chaves do
    /// dicionário — só às propriedades de objeto —, então o nome de campo
    /// observado pelo cliente REST já era o nome da propriedade C#, ex.:
    /// <c>"Title"</c>, não <c>"title"</c>).
    /// </summary>
    public const string DueDateFieldName = nameof(ApplicationCreateTaskRequest.DueDate);

    /// <summary>
    /// Nome do campo usado no dicionário de erros (mesmo formato de
    /// <see cref="DueDateFieldName"/>) quando <see cref="ProtoGetTaskRequest.Id"/>
    /// não é um <see cref="Guid"/> válido (BE-41).
    /// </summary>
    public const string TaskIdFieldName = "Id";

    /// <summary>
    /// Nome do campo usado no dicionário de erros (BE-22) quando
    /// <see cref="ProtoListTasksRequest.Status"/> não é um valor definido de
    /// <see cref="ProtoTaskStatusFilter"/>.
    /// </summary>
    public const string StatusFieldName = "Status";

    /// <summary>
    /// Nome do campo usado no dicionário de erros (BE-22) quando algum
    /// elemento de <see cref="ProtoListTasksRequest.Priority"/> é
    /// <see cref="ProtoTaskPriority.Unspecified"/> ou não é um valor definido
    /// de <see cref="ProtoTaskPriority"/> — um filtro de prioridade "não
    /// especificada" não tem sentido (nenhuma tarefa tem essa prioridade), e
    /// por isso é erro de validação, não um elemento ignorado silenciosamente
    /// (CA-11).
    /// </summary>
    public const string PriorityFieldName = "Priority";

    /// <summary>
    /// Converte o request gerado pelo proto no request da Application
    /// (BE-17), ou devolve o erro de validação do campo <c>due_date</c>
    /// quando ele não está no formato <c>yyyy-MM-dd</c> (BE-35, CA-03) — um
    /// erro de validação, nunca uma exceção.
    /// </summary>
    public static TaskGrpcMappingResult ToApplicationRequest(ProtoCreateTaskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateOnly? dueDate = null;

        if (request.HasDueDate)
        {
            if (!DateOnly.TryParseExact(
                    request.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                var errors = new Dictionary<string, string[]>
                {
                    [DueDateFieldName] = ["A data de vencimento deve estar no formato 'yyyy-MM-dd'."],
                };

                return TaskGrpcMappingResult.Invalid(errors);
            }

            dueDate = parsed;
        }

        // TASK_PRIORITY_UNSPECIFIED (valor 0 do enum proto) vira null — o
        // domínio aplica a prioridade Média (RN-TASK-04), o mesmo
        // comportamento de "priority ausente" no contrato REST anterior.
        TaskPriority? priority = request.Priority == ProtoTaskPriority.Unspecified
            ? null
            : ToDomainPriority(request.Priority);

        var applicationRequest = new ApplicationCreateTaskRequest(
            request.Title,
            request.HasDescription ? request.Description : null,
            priority,
            dueDate);

        return TaskGrpcMappingResult.Valid(applicationRequest);
    }

    /// <summary>
    /// Converte o <see cref="ApplicationTaskResponse"/> (BE-17) na
    /// <see cref="ProtoTaskReply"/> devolvida ao chamador (BE-35, CA-02) —
    /// sem campo de dono, por desenho do contrato (RN-AUTZ-01).
    /// </summary>
    public static ProtoTaskReply ToTaskReply(ApplicationTaskResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var reply = new ProtoTaskReply
        {
            Id = response.Id.ToString(),
            Title = response.Title,
            Priority = ToProtoPriority(response.Priority),
            Status = ToProtoStatus(response.Status),
            IsOverdue = response.IsOverdue,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(response.CreatedAt, DateTimeKind.Utc)),
            UpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(response.UpdatedAt, DateTimeKind.Utc)),
        };

        if (response.Description is not null)
        {
            reply.Description = response.Description;
        }

        if (response.DueDate is not null)
        {
            reply.DueDate = response.DueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (response.CompletedAt is not null)
        {
            reply.CompletedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(response.CompletedAt.Value, DateTimeKind.Utc));
        }

        return reply;
    }

    /// <summary>
    /// Converte o request de <c>ListTasks</c> (BE-22). <c>page</c>/<c>page_size</c>
    /// fora da faixa continuam validados pelo <c>ListTasksHandler</c> (CA-07
    /// de BE-41), não aqui — mas <c>status</c> e cada elemento de
    /// <c>priority</c> só têm forma válida como um valor definido do enum
    /// proto correspondente, e isso <b>é</b> responsabilidade deste
    /// mapeamento (mesmo desenho de <c>due_date</c> em
    /// <see cref="ToApplicationRequest(ProtoCreateTaskRequest)"/>: nunca uma
    /// exceção não tratada, sempre um dicionário de erros no formato de
    /// <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>).
    /// </summary>
    public static ListTasksGrpcMappingResult ToApplicationRequest(ProtoListTasksRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();

        var status = ToApplicationStatusFilter(request.Status);
        if (status is null)
        {
            errors[StatusFieldName] = ["O parâmetro 'status' tem um valor inválido."];
        }

        var priorities = new List<TaskPriority>(request.Priority.Count);
        foreach (var protoPriority in request.Priority)
        {
            if (protoPriority == ProtoTaskPriority.Unspecified || !System.Enum.IsDefined(protoPriority))
            {
                errors[PriorityFieldName] = ["O parâmetro 'priority' tem um valor inválido."];
                break;
            }

            priorities.Add(ToDomainPriority(protoPriority));
        }

        if (errors.Count > 0)
        {
            return ListTasksGrpcMappingResult.Invalid(errors);
        }

        var applicationRequest = new ApplicationListTasksRequest(
            request.Page,
            request.PageSize,
            status!.Value,
            priorities,
            request.HasOverdue ? request.Overdue : null,
            request.Search);

        return ListTasksGrpcMappingResult.Valid(applicationRequest);
    }

    private static ApplicationTaskStatusFilter? ToApplicationStatusFilter(ProtoTaskStatusFilter status) => status switch
    {
        ProtoTaskStatusFilter.Unspecified => ApplicationTaskStatusFilter.All,
        ProtoTaskStatusFilter.All => ApplicationTaskStatusFilter.All,
        ProtoTaskStatusFilter.Pending => ApplicationTaskStatusFilter.Pending,
        ProtoTaskStatusFilter.Completed => ApplicationTaskStatusFilter.Completed,
        _ => null,
    };

    /// <summary>
    /// Converte o <see cref="ApplicationListTasksResponse"/> na
    /// <see cref="ProtoListTasksReply"/> devolvida ao chamador (BE-41), com
    /// cada item passando por <see cref="ToTaskReply"/> — o mesmo mapeamento
    /// de <c>CreateTask</c>, sem duplicação.
    /// </summary>
    public static ProtoListTasksReply ToListTasksReply(ApplicationListTasksResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var reply = new ProtoListTasksReply
        {
            Page = response.Page,
            PageSize = response.PageSize,
            TotalCount = response.TotalCount,
        };

        reply.Items.AddRange(response.Items.Select(ToTaskReply));

        return reply;
    }

    /// <summary>
    /// Tenta converter <see cref="ProtoGetTaskRequest.Id"/> num
    /// <see cref="Guid"/> — um valor que não parseia é erro de validação
    /// (BE-41, nota técnica: não é papel deste RPC decidir 400 de rota vs.
    /// 404, isso é do Gateway), nunca uma exceção não tratada.
    /// </summary>
    public static bool TryParseTaskId(ProtoGetTaskRequest request, out Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(request.Id, out taskId);
    }

    /// <summary>Mesma conversão de <see cref="TryParseTaskId(ProtoGetTaskRequest, out Guid)"/>, para <c>UpdateTask</c> (BE-19).</summary>
    public static bool TryParseTaskId(ProtoUpdateTaskRequest request, out Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(request.Id, out taskId);
    }

    /// <summary>Mesma conversão de <see cref="TryParseTaskId(ProtoGetTaskRequest, out Guid)"/>, para <c>CompleteTask</c> (BE-20).</summary>
    public static bool TryParseTaskId(ProtoCompleteTaskRequest request, out Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(request.Id, out taskId);
    }

    /// <summary>Mesma conversão de <see cref="TryParseTaskId(ProtoGetTaskRequest, out Guid)"/>, para <c>ReopenTask</c> (BE-20).</summary>
    public static bool TryParseTaskId(ProtoReopenTaskRequest request, out Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(request.Id, out taskId);
    }

    /// <summary>Mesma conversão de <see cref="TryParseTaskId(ProtoGetTaskRequest, out Guid)"/>, para <c>DeleteTask</c> (BE-21).</summary>
    public static bool TryParseTaskId(ProtoDeleteTaskRequest request, out Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Guid.TryParse(request.Id, out taskId);
    }

    /// <summary>
    /// Converte o request de <c>UpdateTask</c> (BE-19) — mesma lógica de
    /// <c>due_date</c>/<c>priority</c> de <see cref="ToApplicationRequest(ProtoCreateTaskRequest)"/>
    /// (nenhuma regra nova, só reaplicada a uma mensagem diferente):
    /// <c>due_date</c> fora do formato <c>yyyy-MM-dd</c> é erro de validação
    /// (nunca exceção), e <c>TASK_PRIORITY_UNSPECIFIED</c> vira
    /// <see cref="TaskPriority.Medium"/> — o padrão de substituição do PUT
    /// (BE-19, CA-05), já resolvido aqui porque
    /// <see cref="Domain.Tasks.TodoTask.UpdateDetails"/> exige uma prioridade
    /// concreta (ao contrário de <c>TodoTask.Create</c>, que aceita nula e
    /// aplica o padrão internamente).
    /// </summary>
    public static UpdateTaskGrpcMappingResult ToApplicationRequest(ProtoUpdateTaskRequest request, Guid taskId)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateOnly? dueDate = null;

        if (request.HasDueDate)
        {
            if (!DateOnly.TryParseExact(
                    request.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                var errors = new Dictionary<string, string[]>
                {
                    [DueDateFieldName] = ["A data de vencimento deve estar no formato 'yyyy-MM-dd'."],
                };

                return UpdateTaskGrpcMappingResult.Invalid(errors);
            }

            dueDate = parsed;
        }

        var priority = request.Priority == ProtoTaskPriority.Unspecified
            ? TaskPriority.Medium
            : ToDomainPriority(request.Priority);

        var applicationRequest = new ApplicationUpdateTaskRequest(
            taskId, request.Title, request.HasDescription ? request.Description : null, priority, dueDate);

        return UpdateTaskGrpcMappingResult.Valid(applicationRequest);
    }

    private static TaskPriority ToDomainPriority(ProtoTaskPriority priority) => priority switch
    {
        ProtoTaskPriority.Low => TaskPriority.Low,
        ProtoTaskPriority.Medium => TaskPriority.Medium,
        ProtoTaskPriority.High => TaskPriority.High,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "TaskPriority (proto) sem mapeamento de domínio definido."),
    };

    private static ProtoTaskPriority ToProtoPriority(TaskPriority priority) => priority switch
    {
        TaskPriority.Low => ProtoTaskPriority.Low,
        TaskPriority.Medium => ProtoTaskPriority.Medium,
        TaskPriority.High => ProtoTaskPriority.High,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "TaskPriority (domínio) sem mapeamento proto definido."),
    };

    private static ProtoTaskStatus ToProtoStatus(TodoTaskStatus status) => status switch
    {
        TodoTaskStatus.Pending => ProtoTaskStatus.Pending,
        TodoTaskStatus.Completed => ProtoTaskStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "TodoTaskStatus (domínio) sem mapeamento proto definido."),
    };
}

/// <summary>
/// Resultado de <see cref="TaskGrpcMapping.ToApplicationRequest"/> — nunca
/// lança para "due_date fora do formato" (BE-35, CA-03): ou há um
/// <see cref="ApplicationCreateTaskRequest"/> válido, ou há o dicionário de
/// erros no mesmo formato de <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>.
/// </summary>
public sealed class TaskGrpcMappingResult
{
    private TaskGrpcMappingResult(ApplicationCreateTaskRequest? request, IReadOnlyDictionary<string, string[]>? errors)
    {
        Request = request;
        Errors = errors;
    }

    public bool IsValid => Request is not null;

    public ApplicationCreateTaskRequest? Request { get; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static TaskGrpcMappingResult Valid(ApplicationCreateTaskRequest request) => new(request, null);

    public static TaskGrpcMappingResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}

/// <summary>
/// Resultado de <see cref="TaskGrpcMapping.ToApplicationRequest(TodoList.Contracts.Tasks.V1.UpdateTaskRequest, Guid)"/> —
/// mesmo desenho de <see cref="TaskGrpcMappingResult"/>, para <c>UpdateTask</c>
/// (BE-19): nunca lança para "due_date fora do formato", só devolve o
/// dicionário de erros no mesmo formato de
/// <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>.
/// </summary>
public sealed class UpdateTaskGrpcMappingResult
{
    private UpdateTaskGrpcMappingResult(ApplicationUpdateTaskRequest? request, IReadOnlyDictionary<string, string[]>? errors)
    {
        Request = request;
        Errors = errors;
    }

    public bool IsValid => Request is not null;

    public ApplicationUpdateTaskRequest? Request { get; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static UpdateTaskGrpcMappingResult Valid(ApplicationUpdateTaskRequest request) => new(request, null);

    public static UpdateTaskGrpcMappingResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}

/// <summary>
/// Resultado de <see cref="TaskGrpcMapping.ToApplicationRequest(TodoList.Contracts.Tasks.V1.ListTasksRequest)"/> —
/// mesmo desenho de <see cref="TaskGrpcMappingResult"/>, para <c>ListTasks</c>
/// (BE-22): nunca lança para <c>status</c>/<c>priority</c> fora do enum, só
/// devolve o dicionário de erros no mesmo formato de
/// <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>.
/// </summary>
public sealed class ListTasksGrpcMappingResult
{
    private ListTasksGrpcMappingResult(ApplicationListTasksRequest? request, IReadOnlyDictionary<string, string[]>? errors)
    {
        Request = request;
        Errors = errors;
    }

    public bool IsValid => Request is not null;

    public ApplicationListTasksRequest? Request { get; }

    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static ListTasksGrpcMappingResult Valid(ApplicationListTasksRequest request) => new(request, null);

    public static ListTasksGrpcMappingResult Invalid(IReadOnlyDictionary<string, string[]> errors) => new(null, errors);
}
