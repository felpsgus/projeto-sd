using TodoList.Gateway.Api.Contracts;
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

    /// <summary>
    /// Converte o request já validado e resolvido de <c>GET /api/tasks</c>
    /// (BE-41 CA-17/CA-19; BE-22 filtros/busca) no request proto —
    /// <see cref="ListTasksHttpRequest.Page"/>/<see cref="ListTasksHttpRequest.PageSize"/>
    /// chegam aqui já com os padrões do Gateway aplicados (nunca <c>0</c>),
    /// então o Tasks nunca precisa aplicar o próprio padrão numa chamada
    /// vinda do Gateway — só num chamador gRPC direto (defesa em
    /// profundidade, D-09). <see cref="ListTasksHttpRequest.Overdue"/> nulo
    /// (BE-22, "não informado") deixa o campo <c>optional bool</c> do proto
    /// sem valor — <c>HasOverdue</c> só fica <see langword="true"/> quando um
    /// valor explícito foi validado, nunca por setar <c>false</c> à toa (o
    /// que colidiria com um <c>overdue=false</c> explícito do lado do
    /// Tasks). <see cref="ListTasksHttpRequest.Search"/> nulo vira string
    /// vazia — o mesmo "ausente" que <c>ListTasksRequest.search</c> já
    /// representa sem precisar de <c>optional</c> (ver o comentário do campo
    /// em <c>tasks.proto</c>).
    /// </summary>
    public static ProtoListTasksRequest ToProtoRequest(ListTasksHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proto = new ProtoListTasksRequest
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Status = ToProtoStatusFilter(request.Status),
            Search = request.Search ?? string.Empty,
        };

        proto.Priority.AddRange(request.Priority.Select(ToProtoPriority));

        if (request.Overdue is not null)
        {
            proto.Overdue = request.Overdue.Value;
        }

        return proto;
    }

    private static ProtoTaskStatusFilter ToProtoStatusFilter(string status) => status switch
    {
        "pending" => ProtoTaskStatusFilter.Pending,
        "completed" => ProtoTaskStatusFilter.Completed,
        "all" => ProtoTaskStatusFilter.All,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Status sem mapeamento proto definido — deveria ter sido barrado pelo validador."),
    };

    /// <summary>Converte a resposta de <c>ListTasks</c> (BE-41) no DTO HTTP devolvido ao cliente (CA-17/CA-24).</summary>
    public static ListTasksHttpResponse ToHttpResponse(ProtoListTasksReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return new ListTasksHttpResponse(
            reply.Items.Select(ToHttpResponse).ToList(),
            reply.Page,
            reply.PageSize,
            reply.TotalCount);
    }

    /// <summary>Converte o id de rota (já validado como Guid pelo endpoint, CA-22) no request proto de <c>GetTask</c> (BE-41).</summary>
    public static ProtoGetTaskRequest ToProtoGetTaskRequest(string id) => new() { Id = id };

    /// <summary>
    /// Converte o id de rota (já validado como Guid) e o DTO HTTP já validado
    /// (<see cref="Validation.UpdateTaskHttpRequestValidator"/>) no request
    /// proto de <c>UpdateTask</c> (BE-19) — mesma lógica de
    /// <see cref="ToProtoRequest(CreateTaskHttpRequest)"/> para
    /// <see cref="UpdateTaskHttpRequest.Priority"/>/<see cref="UpdateTaskHttpRequest.Description"/>/
    /// <see cref="UpdateTaskHttpRequest.DueDate"/>: prioridade ausente vira
    /// <see cref="ProtoTaskPriority.Unspecified"/> (o Tasks aplica o padrão
    /// Média nesta substituição, BE-19 nota técnica); descrição e vencimento
    /// nulos deixam o campo <c>optional</c> sem valor, o que o Tasks lê como
    /// "limpar o campo" (semântica de substituição do PUT).
    /// </summary>
    public static ProtoUpdateTaskRequest ToProtoRequest(string id, UpdateTaskHttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proto = new ProtoUpdateTaskRequest
        {
            Id = id,
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

    /// <summary>Converte o id de rota (já validado como Guid) no request proto de <c>CompleteTask</c> (BE-20).</summary>
    public static ProtoCompleteTaskRequest ToProtoCompleteTaskRequest(string id) => new() { Id = id };

    /// <summary>Converte o id de rota (já validado como Guid) no request proto de <c>ReopenTask</c> (BE-20).</summary>
    public static ProtoReopenTaskRequest ToProtoReopenTaskRequest(string id) => new() { Id = id };

    /// <summary>Converte o id de rota (já validado como Guid) no request proto de <c>DeleteTask</c> (BE-21).</summary>
    public static ProtoDeleteTaskRequest ToProtoDeleteTaskRequest(string id) => new() { Id = id };

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
