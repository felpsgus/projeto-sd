using TodoList.Gateway.Api.Configuration;
using TodoList.Gateway.Api.Contracts;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validação de toda a query string de <c>GET /api/tasks</c> na borda do
/// Gateway (BE-41 CA-19; BE-22 CA-11/CA-17) — método puro, sem
/// FluentValidation: os parâmetros são primitivos vindos da query string, e a
/// distinção "informado com valor inválido" (400) vs. "ausente" (aplica o
/// padrão da BE-22) não precisa do aparato de
/// <see cref="FluentValidation.AbstractValidator{T}"/> para ficar clara ou
/// testável (mesmo espírito de "Data Annotations em casos simples" de
/// CONVENCOES-CODIGO.md §2.1). <c>status</c>, <c>priority</c> e
/// <c>overdue</c> chegam ao endpoint como <c>string</c> crua (nunca
/// <c>bool?</c>/enum diretamente no parâmetro do Minimal API) pelo mesmo
/// motivo documentado em <see cref="CreateTaskHttpRequest"/>: um valor fora
/// do domínio esperado precisa virar erro por campo, não uma falha de
/// binding sem detalhe. Os limites de página vêm de <see cref="PagingOptions"/>
/// — a cópia própria do Gateway (D-33), não a do Tasks.
/// </summary>
public static class ListTasksQueryValidator
{
    /// <summary>Nome do campo (já em camelCase) usado quando <c>page</c> é menor que 1.</summary>
    public const string PageFieldName = "page";

    /// <summary>Nome do campo usado quando <c>pageSize</c> está fora de 1..<see cref="PagingOptions.MaxPageSize"/>.</summary>
    public const string PageSizeFieldName = "pageSize";

    /// <summary>Nome do campo usado quando <c>status</c> não é um dos valores de BE-22 (RN-LIST-02).</summary>
    public const string StatusFieldName = "status";

    /// <summary>Nome do campo usado quando algum elemento de <c>priority</c> não é um dos valores de BE-22 (RN-LIST-03).</summary>
    public const string PriorityFieldName = "priority";

    /// <summary>Nome do campo usado quando <c>overdue</c> não é <c>"true"</c> nem <c>"false"</c> (RN-LIST-04).</summary>
    public const string OverdueFieldName = "overdue";

    private static readonly string[] _validStatuses = ["pending", "completed", "all"];
    private static readonly string[] _validPriorities = ["low", "medium", "high"];

    /// <summary>
    /// Valida a query string inteira de <c>GET /api/tasks</c> e, quando
    /// válida, já devolve o <see cref="ListTasksHttpRequest"/> resolvido
    /// (padrões aplicados) — para que o endpoint nunca precise reaplicar a
    /// mesma lógica de resolução "ausente vs. informado" (BE-22, tabela da
    /// query string).
    /// </summary>
    public static ListTasksQueryValidationResult Validate(
        int? page,
        int? pageSize,
        string? status,
        IReadOnlyList<string>? priority,
        string? overdue,
        string? search,
        PagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new Dictionary<string, string[]>();

        if (page is < 1)
        {
            errors[PageFieldName] = ["O parâmetro 'page' deve ser maior ou igual a 1."];
        }

        if (pageSize is < 1 || pageSize > options.MaxPageSize)
        {
            errors[PageSizeFieldName] = [$"O parâmetro 'pageSize' deve estar entre 1 e {options.MaxPageSize}."];
        }

        // RN-LIST-02: ausente equivale a "all" — os dois têm o mesmo efeito
        // (sem filtro de estado), mas só um valor de fato informado e fora
        // do vocabulário é erro (CA-11).
        var resolvedStatus = "all";
        if (status is not null)
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();

            if (!_validStatuses.Contains(normalizedStatus, StringComparer.Ordinal))
            {
                errors[StatusFieldName] =
                    [$"O parâmetro 'status' deve ser um dos seguintes: {string.Join(", ", _validStatuses)}."];
            }
            else
            {
                resolvedStatus = normalizedStatus;
            }
        }

        // RN-LIST-03: lista ausente/vazia equivale a "todas as prioridades"
        // — nenhum elemento é erro; um elemento fora do vocabulário é erro
        // (CA-11), mesmo que outros elementos da mesma lista sejam válidos.
        var resolvedPriorities = new List<string>();
        if (priority is not null)
        {
            foreach (var value in priority)
            {
                var normalizedPriority = value.Trim().ToLowerInvariant();

                if (!_validPriorities.Contains(normalizedPriority, StringComparer.Ordinal))
                {
                    errors[PriorityFieldName] =
                        [$"O parâmetro 'priority' deve ser um dos seguintes: {string.Join(", ", _validPriorities)}."];
                    break;
                }

                resolvedPriorities.Add(normalizedPriority);
            }
        }

        // RN-LIST-04: ausente é "não filtra por atraso" (null); "true"/"false"
        // informado é um valor explícito — a mesma distinção do proto
        // `optional bool overdue`, resolvida aqui em vez de deixar o Minimal
        // API tentar (e falhar silenciosamente) o binding direto para bool?.
        bool? resolvedOverdue = null;
        if (overdue is not null)
        {
            if (bool.TryParse(overdue, out var parsedOverdue))
            {
                resolvedOverdue = parsedOverdue;
            }
            else
            {
                errors[OverdueFieldName] = ["O parâmetro 'overdue' deve ser 'true' ou 'false'."];
            }
        }

        // RN-LIST-05 / CA-17 (BE-22): vazio ou só espaços equivale a ausente.
        var resolvedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        if (errors.Count > 0)
        {
            return ListTasksQueryValidationResult.Invalid(errors);
        }

        var request = new ListTasksHttpRequest(
            page ?? 1,
            pageSize ?? options.DefaultPageSize,
            resolvedStatus,
            resolvedPriorities,
            resolvedOverdue,
            resolvedSearch);

        return ListTasksQueryValidationResult.Valid(request);
    }
}

/// <summary>
/// Resultado de <see cref="ListTasksQueryValidator.Validate"/> — mesmo
/// desenho de <c>TaskGrpcMapping.ListTasksGrpcMappingResult</c> do Tasks
/// (BE-22): ou há um <see cref="ListTasksHttpRequest"/> já resolvido, ou há o
/// dicionário de erros por campo (formato de <c>Results.ValidationProblem</c>).
/// </summary>
public sealed class ListTasksQueryValidationResult
{
    private ListTasksQueryValidationResult(ListTasksHttpRequest? request, IDictionary<string, string[]>? errors)
    {
        Request = request;
        Errors = errors;
    }

    public bool IsValid => Request is not null;

    public ListTasksHttpRequest? Request { get; }

    public IDictionary<string, string[]>? Errors { get; }

    public static ListTasksQueryValidationResult Valid(ListTasksHttpRequest request) => new(request, null);

    public static ListTasksQueryValidationResult Invalid(IDictionary<string, string[]> errors) => new(null, errors);
}
