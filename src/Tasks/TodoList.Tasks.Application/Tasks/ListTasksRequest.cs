namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Request de <c>ListTasks</c> (BE-41, recorte de BE-22 — RN-LIST-07). Sem
/// campo de dono, pela mesma razão de <see cref="CreateTaskRequest"/>: o
/// dono nunca vem do request, sempre de <see cref="Security.ICurrentUser"/>.
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
public sealed record ListTasksRequest(int Page, int PageSize);
