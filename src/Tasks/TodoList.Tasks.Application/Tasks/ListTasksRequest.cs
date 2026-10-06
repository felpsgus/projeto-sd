using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Request de <c>ListTasks</c> (BE-22, RN-LIST-01 a RN-LIST-07). Sem campo de
/// dono, pela mesma razão de <see cref="CreateTaskRequest"/>: o dono nunca
/// vem do request, sempre de <see cref="Security.ICurrentUser"/>.
/// </summary>
/// <param name="Page">
/// Página desejada (1-based). <c>0</c> significa "não informado" — o
/// <see cref="ListTasksHandler"/> aplica o padrão <c>page=1</c> (CA-06).
/// Qualquer valor negativo é erro de validação, não é normalizado.
/// </param>
/// <param name="PageSize">
/// Tamanho de página desejado. <c>0</c> significa "não informado" — o
/// handler aplica <see cref="PagingOptions.DefaultPageSize"/> (CA-06). Fora
/// de 1–<see cref="PagingOptions.MaxPageSize"/> é erro de validação (CA-07).
/// </param>
/// <param name="Status">RN-LIST-02. Padrão <see cref="TaskStatusFilter.All"/> — não filtra por estado.</param>
/// <param name="Priorities">
/// RN-LIST-03. <c>null</c>/vazio — o padrão — não filtra por prioridade;
/// valores repetidos formam uma disjunção (<c>priority IN (...)</c>).
/// </param>
/// <param name="Overdue">RN-LIST-04. <c>null</c> — o padrão — não filtra por atraso.</param>
/// <param name="Search">
/// RN-LIST-05/D-16. <c>null</c>, vazio ou só espaços — o padrão — não filtra
/// por texto (CA-17); o handler faz o <c>Trim()</c> antes de repassar ao
/// repositório.
/// </param>
public sealed record ListTasksRequest(
    int Page,
    int PageSize,
    TaskStatusFilter Status = TaskStatusFilter.All,
    IReadOnlyList<TaskPriority>? Priorities = null,
    bool? Overdue = null,
    string? Search = null);
