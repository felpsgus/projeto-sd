namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Filtro de estado de <c>ListTasks</c> (BE-22, RN-LIST-02) — vocabulário de
/// filtro, não o estado real de uma tarefa (<see cref="Domain.Tasks.TodoTaskStatus"/>
/// só tem <c>Pending</c>/<c>Completed</c>; "tarefa com estado All" não existe).
/// <see cref="All"/> é o padrão quando o chamador não informa nada (BE-22,
/// CA-05: "status=all e ausência do parâmetro retornam ambas").
/// </summary>
public enum TaskStatusFilter
{
    All = 0,
    Pending = 1,
    Completed = 2,
}
