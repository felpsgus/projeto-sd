using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using TodoList.Tasks.Domain.Tasks;
using ApplicationCreateTaskRequest = TodoList.Tasks.Application.Tasks.CreateTaskRequest;
using ApplicationTaskResponse = TodoList.Tasks.Application.Tasks.TaskResponse;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;

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
    /// REST devolvia (<c>Api/Validation/ValidationFilter.cs</c> serializa as
    /// chaves de <c>FluentValidation.Results.ValidationResult.ToDictionary()</c>
    /// sem nenhuma política de <i>camelCase</i> aplicada às chaves do
    /// dicionário — só às propriedades de objeto —, então o nome de campo
    /// observado pelo cliente REST já era o nome da propriedade C#, ex.:
    /// <c>"Title"</c>, não <c>"title"</c>).
    /// </summary>
    public const string DueDateFieldName = nameof(ApplicationCreateTaskRequest.DueDate);

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
