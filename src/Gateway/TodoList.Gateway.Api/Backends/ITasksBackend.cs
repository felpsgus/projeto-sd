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
}
