namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Parâmetros já validados e resolvidos de <c>GET /api/tasks</c> (BE-41,
/// CA-17/CA-19) — ao contrário de <see cref="CreateTaskHttpRequest"/>, este
/// tipo só existe depois da validação de borda
/// (<see cref="Validation.ListTasksQueryValidator"/>): <see cref="Page"/> e
/// <see cref="PageSize"/> já têm os padrões aplicados quando a query string
/// não os informa, nunca <c>0</c> ou negativo.
/// </summary>
public sealed record ListTasksHttpRequest(int Page, int PageSize);
