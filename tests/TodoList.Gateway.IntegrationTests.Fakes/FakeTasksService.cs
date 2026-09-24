using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using TodoList.Contracts.Tasks.V1;

namespace TodoList.Gateway.IntegrationTests.Fakes;

/// <summary>Requisição de <c>CreateTask</c>, já traduzida do proto para um POCO simples (BE-36).</summary>
public sealed record FakeCreateTaskRequest(string Title, string? Description, string Priority, string? DueDate);

/// <summary>Resposta de <c>CreateTask</c>/<c>GetTask</c> a devolver, como POCO simples (BE-36/BE-41).</summary>
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

/// <summary>Requisição de <c>ListTasks</c>, já traduzida do proto para um POCO simples (BE-41).</summary>
public sealed record FakeListTasksRequest(int Page, int PageSize);

/// <summary>Resposta de <c>ListTasks</c> a devolver, como POCO simples (BE-41).</summary>
public sealed record FakeListTasksReply(IReadOnlyList<FakeTaskReply> Items, int Page, int PageSize, int TotalCount);

/// <summary>Requisição de <c>UpdateTask</c>, já traduzida do proto para um POCO simples (BE-19).</summary>
public sealed record FakeUpdateTaskRequest(string Id, string Title, string? Description, string Priority, string? DueDate);

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

    /// <summary>Handler de <c>ListTasks</c> (BE-41) — configurável por teste; pode lançar <see cref="RpcException"/> diretamente, mesmo padrão de <see cref="CreateTaskHandler"/>.</summary>
    public Func<FakeListTasksRequest, FakeListTasksReply>? ListTasksHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>ListTasks</c> (CA-25/D-34).</summary>
    public Metadata? LastListTasksRequestHeaders { get; private set; }

    public int ListTasksCallCount { get; private set; }

    /// <summary>Handler de <c>GetTask</c> (BE-41) — recebe o <c>id</c> já como <c>string</c>; pode lançar <see cref="RpcException"/> diretamente (CA-21: mesmo <c>NotFound</c> para inexistente/alheia).</summary>
    public Func<string, FakeTaskReply>? GetTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>GetTask</c> (CA-25/D-34).</summary>
    public Metadata? LastGetTaskRequestHeaders { get; private set; }

    public int GetTaskCallCount { get; private set; }

    /// <summary>Handler de <c>UpdateTask</c> (BE-19) — configurável por teste; pode lançar <see cref="RpcException"/> diretamente, mesmo padrão dos demais handlers.</summary>
    public Func<FakeUpdateTaskRequest, FakeTaskReply>? UpdateTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>UpdateTask</c>.</summary>
    public Metadata? LastUpdateTaskRequestHeaders { get; private set; }

    public int UpdateTaskCallCount { get; private set; }

    /// <summary>Handler de <c>CompleteTask</c> (BE-20) — recebe só o <c>id</c>; pode lançar <see cref="RpcException"/> diretamente (ex.: <c>FailedPrecondition</c> de transição inválida).</summary>
    public Func<string, FakeTaskReply>? CompleteTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>CompleteTask</c>.</summary>
    public Metadata? LastCompleteTaskRequestHeaders { get; private set; }

    public int CompleteTaskCallCount { get; private set; }

    /// <summary>Handler de <c>ReopenTask</c> (BE-20) — recebe só o <c>id</c>; mesmo padrão de <see cref="CompleteTaskHandler"/>.</summary>
    public Func<string, FakeTaskReply>? ReopenTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>ReopenTask</c>.</summary>
    public Metadata? LastReopenTaskRequestHeaders { get; private set; }

    public int ReopenTaskCallCount { get; private set; }

    /// <summary>Handler de <c>DeleteTask</c> (BE-21) — recebe só o <c>id</c>; devolve <c>Empty</c>, então não há valor a projetar, só a possibilidade de lançar <see cref="RpcException"/>.</summary>
    public Action<string>? DeleteTaskHandler { get; set; }

    /// <summary>Metadata recebida na última chamada a <c>DeleteTask</c>.</summary>
    public Metadata? LastDeleteTaskRequestHeaders { get; private set; }

    public int DeleteTaskCallCount { get; private set; }

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

        return Task.FromResult(ToProtoTaskReply(output));
    }

    /// <summary>BE-41, CA-17/CA-19 — o Gateway já resolveu os padrões antes de chamar (mesmo espírito de CA-25/traceparent: só valida o repasse, não recalcula regra de negócio).</summary>
    public override Task<ListTasksReply> ListTasks(ListTasksRequest request, ServerCallContext context)
    {
        ListTasksCallCount++;
        LastListTasksRequestHeaders = context.RequestHeaders;

        if (ListTasksHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.ListTasksHandler não configurado."));
        }

        var output = ListTasksHandler(new FakeListTasksRequest(request.Page, request.PageSize));

        var reply = new ListTasksReply
        {
            Page = output.Page,
            PageSize = output.PageSize,
            TotalCount = output.TotalCount,
        };
        reply.Items.AddRange(output.Items.Select(ToProtoTaskReply));

        return Task.FromResult(reply);
    }

    /// <summary>BE-41, CA-20/CA-21 — <paramref name="context"/> só devolve o <c>id</c> já validado pelo Gateway (CA-22 barra formato inválido antes de chegar aqui).</summary>
    public override Task<TaskReply> GetTask(GetTaskRequest request, ServerCallContext context)
    {
        GetTaskCallCount++;
        LastGetTaskRequestHeaders = context.RequestHeaders;

        if (GetTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.GetTaskHandler não configurado."));
        }

        var output = GetTaskHandler(request.Id);

        return Task.FromResult(ToProtoTaskReply(output));
    }

    /// <summary>BE-19 — substituição completa; <paramref name="context"/> só devolve o <c>id</c>/campos já validados pelo Gateway.</summary>
    public override Task<TaskReply> UpdateTask(UpdateTaskRequest request, ServerCallContext context)
    {
        UpdateTaskCallCount++;
        LastUpdateTaskRequestHeaders = context.RequestHeaders;

        if (UpdateTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.UpdateTaskHandler não configurado."));
        }

        var input = new FakeUpdateTaskRequest(
            request.Id,
            request.Title,
            request.HasDescription ? request.Description : null,
            request.Priority.ToString(),
            request.HasDueDate ? request.DueDate : null);

        var output = UpdateTaskHandler(input);

        return Task.FromResult(ToProtoTaskReply(output));
    }

    /// <summary>BE-20 — sem corpo de requisição além do <c>id</c>; o handler pode lançar <c>FailedPrecondition</c> para simular transição inválida.</summary>
    public override Task<TaskReply> CompleteTask(CompleteTaskRequest request, ServerCallContext context)
    {
        CompleteTaskCallCount++;
        LastCompleteTaskRequestHeaders = context.RequestHeaders;

        if (CompleteTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.CompleteTaskHandler não configurado."));
        }

        var output = CompleteTaskHandler(request.Id);

        return Task.FromResult(ToProtoTaskReply(output));
    }

    /// <summary>BE-20 — mesmo padrão de <see cref="CompleteTask"/>, espelhado.</summary>
    public override Task<TaskReply> ReopenTask(ReopenTaskRequest request, ServerCallContext context)
    {
        ReopenTaskCallCount++;
        LastReopenTaskRequestHeaders = context.RequestHeaders;

        if (ReopenTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.ReopenTaskHandler não configurado."));
        }

        var output = ReopenTaskHandler(request.Id);

        return Task.FromResult(ToProtoTaskReply(output));
    }

    /// <summary>BE-21 — soft delete; devolve <see cref="Empty"/>, sem nada a projetar.</summary>
    public override Task<Empty> DeleteTask(DeleteTaskRequest request, ServerCallContext context)
    {
        DeleteTaskCallCount++;
        LastDeleteTaskRequestHeaders = context.RequestHeaders;

        if (DeleteTaskHandler is null)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "FakeTasksService.DeleteTaskHandler não configurado."));
        }

        DeleteTaskHandler(request.Id);

        return Task.FromResult(new Empty());
    }

    private static TaskReply ToProtoTaskReply(FakeTaskReply output)
    {
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

        return reply;
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
