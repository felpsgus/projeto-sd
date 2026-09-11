namespace TodoList.Identity.Application.Security;

/// <summary>
/// Validação de access token JWT (BE-08). É o único mecanismo de validação de
/// token do sistema (D-31): quem está fora do Identity não recebe a chave de
/// assinatura, e pergunta a este validador — hoje via <c>ValidateToken</c>
/// gRPC (BE-34), amanhã também pelo Bearer do próprio Identity, quando ele
/// entrar em escopo.
/// </summary>
public interface IAccessTokenValidator
{
    /// <summary>
    /// Valida <paramref name="token"/>: assinatura, issuer, audience e prazo
    /// de validade (<c>ClockSkew</c> zero). Nunca lança exceção — qualquer
    /// entrada malformada (nula, vazia, lixo, assinatura quebrada, expirada,
    /// issuer/audience divergentes) resulta em
    /// <see cref="AccessTokenValidation.Invalid"/>.
    /// </summary>
    public Task<AccessTokenValidation> ValidateAsync(string? token, CancellationToken cancellationToken);
}
