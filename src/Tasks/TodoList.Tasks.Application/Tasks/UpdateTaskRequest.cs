using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Request de <c>UpdateTask</c> (BE-19) — semântica de substituição completa
/// (RN-TASK-11): <see cref="Title"/> é sempre obrigatório, e
/// <see cref="Priority"/> já chega com o padrão de substituição
/// (<see cref="TaskPriority.Medium"/>) resolvido pelo mapeamento gRPC quando o
/// chamador não informa — o mesmo comportamento de "priority ausente" de
/// <see cref="CreateTaskRequest"/>, só que aplicado antes deste tipo existir,
/// porque <see cref="TodoTask.UpdateDetails"/> não aceita prioridade nula (ela
/// não tem "manter o valor atual" para aplicar um padrão depois).
/// </summary>
/// <param name="TaskId">A tarefa a editar; resolvida por <c>GetOwnedTaskAsync</c> (BE-18), nunca por um <c>GetById</c> sem filtro de dono.</param>
/// <param name="Title">Obrigatório; 1–200 caracteres após <c>Trim()</c> (RN-TASK-02, mesma regra de <see cref="CreateTaskRequest"/>).</param>
/// <param name="Description">Opcional; ausente vira <c>null</c> e limpa o campo (semântica de substituição, BE-19).</param>
/// <param name="Priority">Já resolvida (nunca "ausente" neste tipo); <see cref="TaskPriority.Medium"/> é o padrão de substituição.</param>
/// <param name="DueDate">Opcional; ausente vira <c>null</c> e limpa o campo (semântica de substituição, BE-19).</param>
public sealed record UpdateTaskRequest(
    Guid TaskId,
    string Title,
    string? Description,
    TaskPriority Priority,
    DateOnly? DueDate);
