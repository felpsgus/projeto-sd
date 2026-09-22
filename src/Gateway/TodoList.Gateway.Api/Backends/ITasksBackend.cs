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
}
