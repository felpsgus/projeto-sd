using System.Text.Json;

namespace TodoList.Gateway.Api.Validation;

/// <summary>
/// Normaliza as chaves de um dicionário de erros de validação para
/// <c>camelCase</c> — correção adjacente pedida pelo tech lead (BE-41): o
/// resto de todo corpo JSON do Gateway já sai em camelCase (padrão do
/// <c>JsonNamingPolicy</c> do Minimal API sobre propriedades de objeto), mas
/// o dicionário <c>errors</c> de um <c>ValidationProblemDetails</c> chegava
/// em PascalCase, porque nenhuma política de nomenclatura é aplicada às
/// <b>chaves</b> de um <c>Dictionary&lt;string, string[]&gt;</c> — só às
/// propriedades de um objeto. Isso acontecia nas duas origens do dicionário:
/// <list type="bullet">
/// <item><see cref="ValidationFilter{TRequest}"/>, a partir de
/// <c>FluentValidation.Results.ValidationResult.ToDictionary()</c> (chaves
/// são o nome da propriedade C#, ex.: <c>"Title"</c>);</item>
/// <item><see cref="ErrorHandling.GrpcErrorMapping"/>, a partir do trailer
/// gRPC <c>validation-errors</c> que o Tasks devolve (mesmo formato, mesma
/// causa: <c>TaskGrpcMapping.DueDateFieldName</c>/<c>TaskIdFieldName</c> no
/// Tasks Service também nomeiam o campo com <c>nameof(...)</c>, em
/// PascalCase).</item>
/// </list>
/// Um único ponto de normalização evita que as duas origens precisem lembrar
/// de fazer a mesma coisa, e cobre qualquer origem futura sem mudança nela.
/// </summary>
public static class ValidationErrorKeyNormalizer
{
    /// <summary>Converte cada chave do dicionário para camelCase, preservando a ordem original e os valores.</summary>
    public static IDictionary<string, string[]> ToCamelCaseKeys(IDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.ToDictionary(
            pair => JsonNamingPolicy.CamelCase.ConvertName(pair.Key),
            pair => pair.Value);
    }
}
