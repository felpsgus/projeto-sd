namespace TodoList.Identity.Application.Security;

/// <summary>
/// Resultado da emissão de um access token (BE-08): o JWT compacto e o
/// instante em que ele deixa de ser válido, para o chamador (ex.: o RPC
/// <c>Login</c> de BE-33) montar a resposta sem ter que decodificar o token
/// de volta.
/// </summary>
/// <param name="Token">JWT compacto assinado, pronto para ser devolvido ao cliente.</param>
/// <param name="ExpiresAt">Instante de expiração (<c>exp</c>), calculado a partir do <see cref="TimeProvider"/> injetado — nunca do relógio do sistema.</param>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// CA-13: o token nunca aparece em log, nem por acidente via
    /// interpolação de string ou logger estruturado que chame
    /// <c>ToString()</c> no objeto. Só a expiração é exposta.
    /// </summary>
    public override string ToString() => $"AccessToken {{ ExpiresAt = {ExpiresAt:O} }}";
}
