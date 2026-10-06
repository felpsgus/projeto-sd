namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON devolvido por <c>GET /api/tasks</c> (BE-41, CA-17/CA-24) — os
/// itens reaproveitam <see cref="TaskHttpResponse"/>, o mesmo DTO que
/// <c>POST /api/tasks</c> já devolve; nenhum tipo gerado pelo proto
/// (<c>ListTasksReply</c>) é serializado diretamente.
/// </summary>
public sealed record ListTasksHttpResponse(
    IReadOnlyList<TaskHttpResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
