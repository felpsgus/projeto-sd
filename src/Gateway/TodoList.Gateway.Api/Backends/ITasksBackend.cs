using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Backends;

/// <summary>
/// Fronteira entre o Gateway e o Tasks Service (BE-36) — a única abstração
/// que <see cref="Endpoints.TaskEndpoints"/> conhece; o cliente gRPC gerado
/// nunca aparece fora de <see cref="TasksBackend"/>.
/// </summary>
public interface ITasksBackend
{
    /// <summary>
    /// Chama <c>CreateTask</c> (BE-35). Lança <see cref="BackendUnavailableException"/>
    /// quando o Tasks está indisponível (D-28) e <see cref="BackendCallException"/>
    /// para qualquer outro <c>RpcException</c> de negócio (D-35) — quem traduz
    /// para HTTP é <see cref="ErrorHandling.GrpcErrorMapping"/>, não este tipo.
    /// </summary>
    public Task<TaskHttpResponse> CreateTaskAsync(CreateTaskHttpRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>ListTasks</c> (BE-41, CA-17). Mesmas exceções de
    /// <see cref="CreateTaskAsync"/> em caso de falha.
    /// </summary>
    public Task<ListTasksHttpResponse> ListTasksAsync(ListTasksHttpRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>GetTask</c> (BE-41, CA-20/CA-21). <paramref name="id"/> já
    /// chegou validado como <see cref="Guid"/> pelo endpoint (CA-22) — um
    /// <c>NotFound</c> do Tasks (tarefa inexistente ou alheia, RN-AUTZ-03)
    /// vira <see cref="BackendCallException"/> igual a qualquer outro erro de
    /// negócio, traduzido a 404 por <see cref="ErrorHandling.GrpcErrorMapping"/>.
    /// </summary>
    public Task<TaskHttpResponse> GetTaskAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>UpdateTask</c> (BE-19) — substituição completa dos campos
    /// editáveis. <paramref name="id"/> já chegou validado como
    /// <see cref="Guid"/> pelo endpoint, mesmo padrão de
    /// <see cref="GetTaskAsync"/>. Mesmas exceções em caso de falha
    /// (<c>NotFound</c> de tarefa inexistente/alheia vira 404, RN-AUTZ-03).
    /// </summary>
    public Task<TaskHttpResponse> UpdateTaskAsync(string id, UpdateTaskHttpRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>CompleteTask</c> (BE-20, RN-TASK-08). Repetir sobre uma
    /// tarefa já concluída vira <see cref="BackendCallException"/> com
    /// <see cref="Grpc.Core.StatusCode.FailedPrecondition"/> (409,
    /// <c>task.already_completed</c>, D-35) — não um 200 idempotente.
    /// </summary>
    public Task<TaskHttpResponse> CompleteTaskAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>ReopenTask</c> (BE-20, RN-TASK-09). Repetir sobre uma tarefa
    /// já pendente vira <see cref="BackendCallException"/> com
    /// <see cref="Grpc.Core.StatusCode.FailedPrecondition"/> (409,
    /// <c>task.not_completed</c>, D-35).
    /// </summary>
    public Task<TaskHttpResponse> ReopenTaskAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Chama <c>DeleteTask</c> (BE-21, RN-TASK-12/13, soft delete) — devolve
    /// <c>google.protobuf.Empty</c>, então não há nada a traduzir de volta: o
    /// endpoint devolve <c>204 No Content</c> quando esta chamada conclui sem
    /// lançar.
    /// </summary>
    public Task DeleteTaskAsync(string id, CancellationToken cancellationToken);
}
