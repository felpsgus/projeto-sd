using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;

namespace TodoList.Gateway.IntegrationTests.Fakes;

/// <summary>Requisição de <c>CreateTask</c>, já traduzida do proto para um POCO simples (BE-36).</summary>
public sealed record FakeCreateTaskRequest(string Title, string? Description, string Priority, string? DueDate);

/// <summary>Resposta de <c>CreateTask</c> a devolver, como POCO simples (BE-36).</summary>
public sealed record FakeTaskReply(
    string Id,
    string Title,
    string? Description,
    string Priority,
    string Status,
    string? DueDate,
    bool IsOverdue,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Dublê in-process do Tasks Service (BE-36). Exposto só por POCOs — mesmo
/// motivo de <see cref="FakeIdentityService"/>: evitar qualquer tipo de
/// mensagem gerado pelo proto no contrato público consumido pelos testes.
/// </summary>
public sealed class FakeTasksService : TasksService.TasksServiceBase
{
    public Func<FakeCreateTaskRequest, FakeTaskReply>? CreateTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada (CA-25/D-34: <c>traceparent</c>, <c>x-user-id</c>, <c>x-client-date</c>).</summary>
    public Metadata? LastCreateTaskRequestHeaders { get; private set; }

    public int CreateTaskCallCount { get; private set; }

    public override Task<TaskReply> CreateTask(CreateTaskRequest request, ServerCallContext context)
    {
        CreateTaskCallCount++;
        LastCreateTaskRequestHeaders = context.RequestHeaders;

        if (CreateTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.CreateTaskHandler não configurado."));
        }

        var input = new FakeCreateTaskRequest(
            request.Title,
            request.HasDescription ? request.Description : null,
            request.Priority.ToString(),
            request.HasDueDate ? request.DueDate : null);

        var output = CreateTaskHandler(input);

        var reply = new TaskReply
        {
            Id = output.Id,
            Title = output.Title,
            Priority = ToProtoPriority(output.Priority),
            Status = ToProtoStatus(output.Status),
            IsOverdue = output.IsOverdue,
            CreatedAt = Timestamp.FromDateTimeOffset(output.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(output.UpdatedAt),
        };

        if (output.Description is not null)
        {
            reply.Description = output.Description;
        }

        if (output.DueDate is not null)
        {
            reply.DueDate = output.DueDate;
        }

        if (output.CompletedAt is not null)
        {
            reply.CompletedAt = Timestamp.FromDateTimeOffset(output.CompletedAt.Value);
        }

        return Task.FromResult(reply);
    }

    private static TaskPriority ToProtoPriority(string priority) => priority.ToUpperInvariant() switch
    {
        "LOW" => TaskPriority.Low,
        "MEDIUM" => TaskPriority.Medium,
        "HIGH" => TaskPriority.High,
        _ => TaskPriority.Unspecified,
    };

    private static TodoList.Contracts.Tasks.V1.TaskStatus ToProtoStatus(string status) => status.ToUpperInvariant() switch
    {
        "COMPLETED" => TodoList.Contracts.Tasks.V1.TaskStatus.Completed,
        _ => TodoList.Contracts.Tasks.V1.TaskStatus.Pending,
    };
}
