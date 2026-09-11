using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Catálogo de erros de autenticação (BE-33) — mesmo padrão de
/// <c>UserErrors</c>/<c>PasswordErrors</c>: nenhuma mensagem de negócio
/// embutida em código fora de um catálogo.
///
/// <para>
/// <see cref="InvalidCredentials"/> é, de propósito, o <b>único</b> erro que
/// <see cref="LoginHandler"/> produz — e-mail vazio/malformado, usuário
/// inexistente, senha errada e usuário inativo resultam todos neste mesmo
/// <see cref="Error"/> (RN-AUTH-09): expor um código diferente por causa
/// permitiria a quem chama descobrir, por tentativa, se um e-mail está
/// cadastrado ou se a conta está desativada.
/// </para>
/// </summary>
public static class AuthErrors
{
    public static readonly Error InvalidCredentials = new(
        "auth.invalid_credentials",
        "E-mail ou senha inválidos.",
        ErrorType.Unauthorized);
}
