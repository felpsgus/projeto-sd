using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Filtros combináveis de <c>ListTasks</c> (BE-22), já resolvidos e
/// validados pelo <see cref="ListTasksHandler"/> — o parâmetro que
/// <see cref="Persistence.ITodoTaskRepository.ListByOwnerAsync"/> recebe para
/// traduzir tudo em <c>WHERE</c> no banco (nota técnica de BE-22: nada
/// avaliado em memória).
/// </summary>
/// <param name="Status">RN-LIST-02. <see cref="TaskStatusFilter.All"/> não filtra por estado.</param>
/// <param name="Priorities">
/// RN-LIST-03. Vazio não filtra por prioridade; do contrário, disjunção
/// entre os valores (<c>priority IN (...)</c>).
/// </param>
/// <param name="Overdue">
/// RN-LIST-04/RN-TASK-16. <c>null</c> não filtra por atraso; <c>true</c>/<c>false</c>
/// filtram pela condição derivada <c>status=Pending AND DueDate &lt; Today</c>.
/// </param>
/// <param name="Search">
/// RN-LIST-05/D-16, já com <c>Trim()</c> aplicado e <c>null</c> quando ausente
/// ou só espaços (CA-17) — o repositório escapa os curingas de <c>LIKE</c> e
/// aplica o filtro em título e descrição, case-insensitive.
/// </param>
/// <param name="Today">
/// "Hoje" na data local do usuário (<see cref="Security.IClientDate.Today"/>,
/// D-18) — o <b>mesmo</b> valor que o <see cref="ListTasksHandler"/> passa
/// para <see cref="TaskResponseMapper.ToResponse(TodoTask, DateOnly)"/> ao
/// projetar cada item (CA-33c): filtro e projeção nunca podem divergir.
/// </param>
public sealed record TaskListFilter(
    TaskStatusFilter Status,
    IReadOnlyList<TaskPriority> Priorities,
    bool? Overdue,
    string? Search,
    DateOnly Today);
