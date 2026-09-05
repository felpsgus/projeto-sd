using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Único ponto de mapeamento de <see cref="TodoTask"/> para
/// <see cref="TaskResponse"/> (BE-17, nota técnica) — todo endpoint que
/// devolve uma tarefa passa por aqui, o que faz o cálculo de
/// <c>isOverdue</c> (D-18) correto em todos eles sem código próprio em cada
/// um.
/// </summary>
public static class TaskResponseMapper
{
    /// <param name="task">A tarefa a mapear.</param>
    /// <param name="today">
    /// "Hoje" na data local do usuário (<see cref="Security.IClientDate.Today"/>,
    /// D-18) — usado só para calcular <see cref="TaskResponse.IsOverdue"/>.
    /// </param>
    public static TaskResponse ToResponse(this TodoTask task, DateOnly today) =>
        new(
            task.Id,
            task.Title,
            task.Description,
            task.Status,
            task.Priority,
            task.DueDate,
            task.CompletedAt,
            task.IsOverdue(today),
            task.CreatedAt,
            task.UpdatedAt);
}
