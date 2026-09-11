using TodoList.Gateway.Api.Contracts;
using ProtoCreateTaskRequest = TodoList.Contracts.Tasks.V1.CreateTaskRequest;
using ProtoTaskPriority = TodoList.Contracts.Tasks.V1.TaskPriority;
using ProtoTaskReply = TodoList.Contracts.Tasks.V1.TaskReply;
using ProtoTaskStatus = TodoList.Contracts.Tasks.V1.TaskStatus;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Tradução explícita DTO HTTP ↔ mensagens proto do RPC <c>CreateTask</c>
/// (BE-36, CA-04/CA-10) — isolada nesta classe estática, testável sem cliente
/// gRPC nenhum, para que nenhum tipo gerado pelo <c>.proto</c> vaze para fora
/// de <see cref="TasksBackend"/>.
/// </summary>
public static class TaskTranslation
{
    /// <summary>
    /// Converte o DTO HTTP já validado (<see cref="Validation.CreateTaskHttpRequestValidator"/>)
    /// no request proto — <see cref="CreateTaskHttpRequest.Priority"/> ausente
    /// vira <see cref="ProtoTaskPriority.Unspecified"/> (o Tasks aplica o
    /// padrão Média, RN-TASK-04); <see cref="CreateTaskHttpRequest.Description"/>
    /// e <see cref="CreateTaskHttpRequest.DueDate"/> nulos deixam o campo
    /// <c>optional</c> do proto sem valor, em vez de setar uma string vazia.
    /// </summary>
    public static ProtoCreateTaskRequest ToProtoRequest(CreateTaskHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proto = new ProtoCreateTaskRequest
        {
            Title = request.Title!.Trim(),
            Priority = ToProtoPriority(request.Priority),
        };

        if (request.Description is not null)
        {
            proto.Description = request.Description;
        }

        if (request.DueDate is not null)
        {
            proto.DueDate = request.DueDate;
        }

        return proto;
    }

    /// <summary>Converte a resposta do Tasks (BE-35) no DTO HTTP devolvido ao cliente (CA-01/CA-04).</summary>
    public static TaskHttpResponse ToHttpResponse(ProtoTaskReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return new TaskHttpResponse(
            reply.Id,
            reply.Title,
            reply.HasDescription ? reply.Description : null,
            ToHttpPriority(reply.Priority),
            ToHttpStatus(reply.Status),
            reply.HasDueDate ? reply.DueDate : null,
            reply.CompletedAt is not null ? reply.CompletedAt.ToDateTimeOffset() : null,
            reply.IsOverdue,
            reply.CreatedAt.ToDateTimeOffset(),
            reply.UpdatedAt.ToDateTimeOffset());
    }

    private static ProtoTaskPriority ToProtoPriority(string? priority) => priority?.Trim().ToUpperInvariant() switch
    {
        null => ProtoTaskPriority.Unspecified,
        "LOW" => ProtoTaskPriority.Low,
        "MEDIUM" => ProtoTaskPriority.Medium,
        "HIGH" => ProtoTaskPriority.High,
        _ => throw new ArgumentOutOfRangeException(
            nameof(priority), priority, "Priority sem mapeamento proto definido — deveria ter sido barrado pelo validador."),
    };

    private static string ToHttpPriority(ProtoTaskPriority priority) => priority switch
    {
        ProtoTaskPriority.Low => "Low",
        ProtoTaskPriority.Medium => "Medium",
        ProtoTaskPriority.High => "High",
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "TaskPriority (proto) sem mapeamento HTTP definido."),
    };

    private static string ToHttpStatus(ProtoTaskStatus status) => status switch
    {
        ProtoTaskStatus.Pending => "Pending",
        ProtoTaskStatus.Completed => "Completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "TaskStatus (proto) sem mapeamento HTTP definido."),
    };
}
