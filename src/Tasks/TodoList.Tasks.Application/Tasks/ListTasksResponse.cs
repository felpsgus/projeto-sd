namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Resposta de <c>ListTasks</c> (BE-41, recorte de BE-22). <see cref="TotalCount"/>
/// é o total de tarefas do dono (não removidas), não o total da página
/// (CA-08) — quem precisar de "total de páginas" calcula a partir dele.
/// </summary>
/// <param name="Items">A página pedida, já mapeada por <see cref="TaskResponseMapper"/> (com <c>isOverdue</c> calculado).</param>
/// <param name="Page">A página efetivamente aplicada (já com o padrão resolvido, se <c>Page</c> do request era 0).</param>
/// <param name="PageSize">O tamanho de página efetivamente aplicado (já com o padrão resolvido, se <c>PageSize</c> do request era 0).</param>
/// <param name="TotalCount">Total de tarefas do dono (não removidas) — não o total da página (CA-08).</param>
public sealed record ListTasksResponse(
    IReadOnlyList<TaskResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
