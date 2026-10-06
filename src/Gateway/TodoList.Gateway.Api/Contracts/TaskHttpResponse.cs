namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON devolvido por <c>POST /api/tasks</c> (BE-36, CA-01/CA-04) — o
/// mesmo formato que o REST do Tasks devolvia antes de BE-35 remover o
/// endpoint HTTP dele. Nunca o <c>TaskReply</c> gerado pelo proto sai
/// serializado diretamente: <see cref="Backends.TaskTranslation"/> é o único
/// tradutor.
/// </summary>
public sealed record TaskHttpResponse(
    string Id,
    string Title,
    string? Description,
    string Priority,
    string Status,
    string? DueDate,
    DateTimeOffset? CompletedAt,
    bool IsOverdue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
