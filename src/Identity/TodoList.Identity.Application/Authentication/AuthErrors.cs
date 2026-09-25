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

    /// <summary>
    /// BE-07, RN-AUTH-02: e-mail já cadastrado. Ao contrário de
    /// <see cref="InvalidCredentials"/>, aqui o vazamento de existência é
    /// aceitável e desejável — o visitante precisa saber que já tem conta
    /// (nota técnica de BE-07; não contradiz RN-AUTH-09, que fala de login).
    /// </summary>
    public static readonly Error EmailAlreadyRegistered = new(
        "auth.email_already_registered",
        "Este e-mail já está cadastrado.",
        ErrorType.Conflict);

    /// <summary>
    /// BE-15/BE-16: senha atual informada não confere — usado tanto pela
    /// troca de senha quanto pela confirmação de senha da exclusão de conta
    /// (BE-16 nota técnica: "mesma proteção da troca de senha", D-19).
    ///
    /// <para>
    /// <b>Decisão do tech lead (onda 2 da Fase 3): 400, não 401.</b> BE-15
    /// deixava o status em aberto ("400/401"); ficou fechado em
    /// <see cref="ErrorType.Validation"/> por dois motivos: (1) a requisição
    /// já está autenticada por um Bearer válido — o que falhou é um campo do
    /// corpo (a senha atual informada), não a credencial que autentica a
    /// chamada; devolver 401 para um erro de campo seria uma resposta que não
    /// bate com a causa. (2) o interceptor do frontend (FE-06) trata todo 401
    /// como "sessão expirada" e redireciona para o login — um 401 aqui
    /// expulsaria o usuário do app no meio do formulário de troca de senha ou
    /// de confirmação de exclusão. O <c>error-code</c>
    /// <c>auth.invalid_current_password</c> continua estável (CA-04 de
    /// BE-15) — só o <see cref="ErrorType"/>/status HTTP muda; ver
    /// <c>IdentityGrpcService</c> para como o campo (<c>currentPassword</c>
    /// ou <c>password</c>) é anexado ao trailer <c>validation-errors</c> sem
    /// que o <c>error-code</c> vire <c>validation.failed</c>.
    /// </para>
    /// </summary>
    public static readonly Error InvalidCurrentPassword = new(
        "auth.invalid_current_password",
        "A senha atual informada está incorreta.",
        ErrorType.Validation);

    /// <summary>BE-15, CA-06: a nova senha não pode ser igual à atual.</summary>
    public static readonly Error NewPasswordSameAsCurrent = new(
        "auth.new_password_same_as_current",
        "A nova senha não pode ser igual à senha atual.",
        ErrorType.Validation);

    /// <summary>
    /// BE-14/BE-15/BE-16: o <c>user_id</c> do token validado pelo Gateway não
    /// corresponde a nenhum usuário existente — só acontece se a conta foi
    /// excluída depois de emitido um access token ainda dentro da validade
    /// (BE-16, CA-09). Mapeado para <see cref="ErrorType.Unauthorized"/>
    /// (401), nunca 404/500: para quem chama, uma sessão de usuário que não
    /// existe mais é indistinguível de uma sessão inválida.
    /// </summary>
    public static readonly Error UserNotFound = new(
        "auth.user_not_found",
        "Usuário não encontrado.",
        ErrorType.Unauthorized);
}
