namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Parâmetros já validados e resolvidos de <c>GET /api/tasks</c> (BE-41,
/// CA-17/CA-19; BE-22 estende com os filtros/busca) — ao contrário de
/// <see cref="CreateTaskHttpRequest"/>, este tipo só existe depois da
/// validação de borda (<see cref="Validation.ListTasksQueryValidator"/>):
/// <see cref="Page"/> e <see cref="PageSize"/> já têm os padrões aplicados
/// quando a query string não os informa, nunca <c>0</c> ou negativo;
/// <see cref="Status"/> já vem resolvido para <c>"all"</c> quando ausente;
/// <see cref="Priority"/> já vem vazia (nunca <see langword="null"/>) quando
/// ausente; <see cref="Overdue"/> distingue "não informado"
/// (<see langword="null"/>) de um valor explícito — a mesma distinção que
/// <c>ListTasksRequest.overdue</c> (proto <c>optional bool</c>) exige do lado
/// do Tasks; <see cref="Search"/> já chega <see langword="null"/> quando
/// ausente ou só espaços (RN-LIST-05, CA-17 de BE-22).
/// </summary>
public sealed record ListTasksHttpRequest(
    int Page,
    int PageSize,
    string Status,
    IReadOnlyList<string> Priority,
    bool? Overdue,
    string? Search);
