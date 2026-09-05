using TodoList.SharedKernel;

namespace TodoList.Identity.Domain.Users;

/// <summary>
/// Catálogo de erros de invariante do agregado <see cref="User"/> (BE-04) —
/// mesmo padrão de <c>TaskErrors</c> (BE-03, CA-07): nenhuma mensagem de erro
/// de negócio embutida em código fora de um catálogo. Vive no Domain, e não na
/// Application, porque quem produz esses erros (<see cref="Email.Create"/>,
/// <see cref="User.Create(Domain.Users.Email,string?,string,TimeProvider)"/>,
/// <see cref="User.Rename"/>) é o próprio Domain — a validação é invariante de
/// entidade, não regra de caso de uso.
/// </summary>
public static class UserErrors
{
    public static readonly Error EmailEmpty = new(
        "user.email_empty",
        "O e-mail não pode ser vazio.",
        ErrorType.Validation);

    public static readonly Error EmailTooLong = new(
        "user.email_too_long",
        $"O e-mail não pode ter mais de {Email.MaxLength} caracteres.",
        ErrorType.Validation);

    public static readonly Error EmailInvalidFormat = new(
        "user.email_invalid_format",
        "O e-mail informado não tem um formato válido.",
        ErrorType.Validation);

    public static readonly Error DisplayNameEmpty = new(
        "user.display_name_empty",
        "O nome de exibição não pode ser vazio.",
        ErrorType.Validation);

    public static readonly Error DisplayNameTooLong = new(
        "user.display_name_too_long",
        $"O nome de exibição não pode ter mais de {User.DisplayNameMaxLength} caracteres.",
        ErrorType.Validation);

    public static readonly Error PasswordHashRequired = new(
        "user.password_hash_required",
        "O hash de senha é obrigatório.",
        ErrorType.Validation);
}
