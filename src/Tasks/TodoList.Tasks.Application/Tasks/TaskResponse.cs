using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// DTO de resposta de uma <see cref="TodoTask"/> (BE-17) — compartilhado por
/// todo endpoint que devolve uma tarefa (BE-17 em diante: BE-18, BE-19,
/// BE-20, BE-22), sempre produzido por
/// <see cref="TaskResponseMapper.ToResponse(TodoTask, DateOnly)"/>, nunca
/// montado à mão em mais de um lugar. <see cref="Status"/> e
/// <see cref="Priority"/> trafegam como <c>string</c> no JSON
/// (<c>JsonStringEnumConverter</c>, configurado em <c>Program.cs</c>).
/// </summary>
public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    TodoTaskStatus Status,
    TaskPriority Priority,
    DateOnly? DueDate,
    DateTime? CompletedAt,
    bool IsOverdue,
    DateTime CreatedAt,
    DateTime UpdatedAt);
