using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Request de <c>POST /api/tasks</c> (BE-17). Sem campo de dono — de
/// propósito: o dono nunca vem do corpo (CA-22), sempre de
/// <see cref="Security.ICurrentUser"/>. Um <c>"ownerId"</c> enviado no JSON
/// é simplesmente ignorado, porque este tipo não declara essa propriedade.
/// </summary>
/// <param name="Title">Obrigatório; 1–200 caracteres após <c>Trim()</c> (RN-TASK-02).</param>
/// <param name="Description">Opcional; no máximo 2000 caracteres (RN-TASK-03).</param>
/// <param name="Priority">
/// Opcional; <see cref="TaskPriority.Medium"/> quando omitida (RN-TASK-04).
/// Trafega como <c>string</c> no JSON (<c>JsonStringEnumConverter</c>,
/// configurado em <c>Program.cs</c>) — um valor fora do enum já falha na
/// desserialização, antes de qualquer validador rodar.
/// </param>
/// <param name="DueDate">Opcional; data pura, sem hora nem fuso (RN-TASK-05/D-06).</param>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    TaskPriority? Priority,
    DateOnly? DueDate);
