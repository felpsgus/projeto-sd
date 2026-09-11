namespace TodoList.Identity.Application.Security;

/// <summary>
/// Resultado de <see cref="IAccessTokenValidator.ValidateAsync"/> (BE-08).
/// Nunca é construído por exceção — todo caminho de entrada malformada
/// (token nulo/vazio/lixo, assinatura inválida, expirado, issuer/audience
/// divergentes) resulta em <see cref="Invalid"/>, nunca em uma exceção que
/// escapa do validador (consumido por BE-34, que por sua vez nunca deve
/// propagar exceção para o chamador gRPC).
/// </summary>
/// <param name="IsValid">Verdadeiro quando o token é íntegro, com assinatura, issuer, audience e prazo de validade corretos.</param>
/// <param name="UserId">O <c>sub</c> do token, já convertido para <see cref="Guid"/>, quando <paramref name="IsValid"/> é verdadeiro; <see langword="null"/> caso contrário.</param>
public sealed record AccessTokenValidation(bool IsValid, Guid? UserId)
{
    /// <summary>Token inválido por qualquer motivo — o motivo não é exposto aqui (cabe só ao log, nunca ao chamador).</summary>
    public static AccessTokenValidation Invalid { get; } = new(IsValid: false, UserId: null);

    /// <summary>Token válido, com o <paramref name="userId"/> extraído do claim <c>sub</c>.</summary>
    public static AccessTokenValidation Valid(Guid userId) => new(IsValid: true, UserId: userId);
}
