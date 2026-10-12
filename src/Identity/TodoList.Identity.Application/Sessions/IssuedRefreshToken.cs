namespace TodoList.Identity.Application.Sessions;

/// <summary>
/// Refresh token recém-emitido: o valor opaco em claro (único momento em que
/// existe fora do cliente) e sua expiração.
/// </summary>
/// <param name="Value">Valor opaco, Base64Url. Nunca em log.</param>
/// <param name="ExpiresAt">Expiração absoluta (UTC).</param>
/// <param name="SessionId">Sessão a que o token pertence.</param>
public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt, Guid SessionId)
{
    /// <summary>O valor nunca aparece em log, nem por interpolação acidental (RN-AUTH-20, CA-18 de BE-10).</summary>
    public override string ToString() => $"IssuedRefreshToken {{ ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>Sucesso de <see cref="RefreshTokenService.RedeemAsync"/>: dono da sessão e o token que substitui o consumido.</summary>
public sealed record RedeemedRefreshToken(Guid UserId, IssuedRefreshToken Next);

/// <summary>Desfecho de <see cref="RefreshTokenService.RevokeSessionOfTokenAsync"/>.</summary>
public enum RevokeSessionOutcome
{
    /// <summary>A cadeia da sessão do token foi revogada.</summary>
    Revoked,

    /// <summary>Token ausente, vazio ou desconhecido — nada a fazer (idempotência).</summary>
    NotFound,

    /// <summary>O token existe mas é de outro usuário — nada foi revogado (BE-11 CA-09).</summary>
    OwnedByAnotherUser,
}
