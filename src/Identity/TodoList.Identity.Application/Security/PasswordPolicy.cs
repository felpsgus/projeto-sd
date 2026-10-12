using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Security;

/// <summary>
/// Política de força de senha (BE-06, RN-AUTH-04): mínimo de
/// <see cref="MinLength"/> caracteres, ao menos uma letra e ao menos um
/// número. Reutilizável por cadastro (BE-07) e troca de senha (BE-15), sem
/// duplicação — a validação de borda (FluentValidation) chama
/// <see cref="Validate"/> e traduz a lista em falhas de request.
/// </summary>
/// <remarks>
/// Devolve <see cref="IReadOnlyList{T}"/> de <see cref="Error"/>, e não um
/// <see cref="Result"/>, de propósito (CA-07): uma senha pode violar várias
/// regras ao mesmo tempo e o chamador precisa reportar todas de uma vez. O
/// <c>SharedKernel</c> (D-26) modela <c>Result</c>/<c>Result&lt;T&gt;</c> com
/// exatamente um <see cref="Error"/> por falha — ampliar isso para múltiplos
/// erros afetaria os dois serviços por um caso de uso só do Identity, então a
/// política fica com seu próprio retorno em vez de forçar essa mudança no
/// contrato compartilhado.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Tamanho mínimo exigido pela RN-AUTH-04.</summary>
    public const int MinLength = 8;

    /// <summary>
    /// Valida <paramref name="password"/> contra a política. Lista vazia
    /// significa senha válida. <see langword="null"/> e string vazia são
    /// sempre rejeitados (CA-06).
    /// </summary>
    public static IReadOnlyList<Error> Validate(string? password)
    {
        var errors = new List<Error>();

        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            errors.Add(PasswordErrors.TooShort);
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsLetter))
        {
            errors.Add(PasswordErrors.MissingLetter);
        }

        if (string.IsNullOrEmpty(password) || !password.Any(char.IsDigit))
        {
            errors.Add(PasswordErrors.MissingNumber);
        }

        return errors;
    }
}
