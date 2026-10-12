namespace TodoList.Identity.Domain.Sessions;

/// <summary>
/// Refresh token de uma sessão (BE-10, RN-AUTH-14..20). Só o <b>hash</b> do
/// valor opaco é guardado (<see cref="TokenHash"/>, RN-AUTH-20) — o valor em
/// claro existe apenas na resposta do login/refresh.
///
/// <para>
/// Cada login cria uma <see cref="SessionId"/> nova (D-15); cada rotação emite
/// um token novo na <b>mesma</b> sessão, encadeado por <see cref="ReplacedByTokenId"/>.
/// Em produção a transição "consumido" é feita por UPDATE condicional atômico
/// no repositório (nunca ler-verificar-escrever); <see cref="Consume"/> descreve
/// a mesma regra em memória e é o que um repositório de teste usa.
/// </para>
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken(Guid id, Guid userId, string tokenHash, Guid sessionId, DateTime expiresAt, DateTime createdAt)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        SessionId = sessionId;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    /// <summary>SHA-256 (hex minúsculo) do valor opaco — nunca o valor (RN-AUTH-20).</summary>
    public string TokenHash { get; }

    /// <summary>Agrupa a cadeia de rotações de um mesmo login (D-15).</summary>
    public Guid SessionId { get; }

    public DateTime ExpiresAt { get; }

    public DateTime CreatedAt { get; }

    /// <summary>Quando foi trocado por um token novo; <c>null</c> = ainda utilizável.</summary>
    public DateTime? ConsumedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public RefreshTokenRevocationReason? RevokedReason { get; private set; }

    /// <summary>Token que o substituiu na rotação (auditoria da cadeia).</summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid sessionId, string tokenHash, DateTime now, TimeSpan lifetime) =>
        new(Guid.NewGuid(), userId, tokenHash, sessionId, now + lifetime, now);

    public bool IsExpired(DateTime now) => ExpiresAt <= now;

    /// <summary>
    /// Consome o token se ainda estiver utilizável (não consumido, não revogado,
    /// não expirado), encadeando o sucessor. Devolve <c>false</c> sem alterar nada
    /// caso contrário.
    /// </summary>
    public bool Consume(Guid replacedByTokenId, DateTime now)
    {
        if (ConsumedAt is not null || RevokedAt is not null || IsExpired(now))
        {
            return false;
        }

        ConsumedAt = now;
        ReplacedByTokenId = replacedByTokenId;

        return true;
    }

    /// <summary>Revoga o token; idempotente — o primeiro motivo registrado é preservado.</summary>
    public void Revoke(RefreshTokenRevocationReason reason, DateTime now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
