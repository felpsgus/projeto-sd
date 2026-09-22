using TodoList.Gateway.Api.Configuration;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Validação de <c>page</c>/<c>pageSize</c> na borda do Gateway (BE-41,
/// CA-19) — método puro, sem FluentValidation: os dois parâmetros são
/// primitivos vindos da query string, e a regra ("informado, mas fora da
/// faixa" vs. "ausente, aplica o padrão") não precisa do aparato de
/// <see cref="FluentValidation.AbstractValidator{T}"/> para ficar clara ou
/// testável (mesmo espírito de "Data Annotations em casos simples" de
/// CONVENCOES-CODIGO.md §2.1). Os limites vêm de <see cref="PagingOptions"/>
/// — a cópia própria do Gateway (D-33), não a do Tasks.
/// </summary>
public static class ListTasksQueryValidator
{
    /// <summary>Nome do campo (já em camelCase — este validador nasce em BE-41 e não herda a pendência de PascalCase do FluentValidation legado) usado quando <c>page</c> é menor que 1.</summary>
    public const string PageFieldName = "page";

    /// <summary>Nome do campo usado quando <c>pageSize</c> está fora de 1..<see cref="PagingOptions.MaxPageSize"/>.</summary>
    public const string PageSizeFieldName = "pageSize";

    /// <summary>
    /// Devolve o dicionário de erros por campo (formato de
    /// <c>Results.ValidationProblem</c>), ou <see langword="null"/> se
    /// <paramref name="page"/>/<paramref name="pageSize"/> estiverem dentro
    /// da faixa (ou ausentes — CA-19 só rejeita valor informado fora do
    /// limite, nunca a ausência).
    /// </summary>
    public static IDictionary<string, string[]>? Validate(int? page, int? pageSize, PagingOptions options)
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

        return errors.Count > 0 ? errors : null;
    }
}
