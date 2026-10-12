using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Security;

/// <summary>
/// Catálogo de erros da política de senha (BE-06, RN-AUTH-04) — mesmo padrão
/// de <c>UserErrors</c>: nenhuma mensagem de negócio embutida em código fora
/// de um catálogo. Fica na Application (e não no Domain, como
/// <c>UserErrors</c>) porque quem produz esses erros é <see cref="PasswordPolicy"/>,
/// um validador de caso de uso reutilizado por BE-07 e BE-15 — a força da
/// senha é regra de borda de aplicação, não invariante do agregado <c>User</c>.
/// </summary>
public static class PasswordErrors
{
    public static readonly Error TooShort = new(
        "password.too_short",
        $"A senha deve ter no mínimo {PasswordPolicy.MinLength} caracteres.",
        ErrorType.Validation);

    public static readonly Error MissingLetter = new(
        "password.missing_letter",
        "A senha deve conter ao menos uma letra.",
        ErrorType.Validation);

    public static readonly Error MissingNumber = new(
        "password.missing_number",
        "A senha deve conter ao menos um número.",
        ErrorType.Validation);
}
