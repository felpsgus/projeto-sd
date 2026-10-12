using TodoList.Identity.Domain.Sessions;

namespace TodoList.Identity.Application.Sessions;

/// <summary>
/// Persistência de <see cref="RefreshToken"/> (BE-10). Como <c>IUserRepository</c>,
/// <see cref="Add"/> e <see cref="RevokeAllForUserAsync"/> só marcam a mudança —
/// quem chama decide quando comitar via <c>IUnitOfWork</c>. As outras duas
/// operações são UPDATEs condicionais imediatos: a atomicidade que a rotação
/// precisa não pode depender de ler-verificar-escrever.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Busca pelo hash do valor. <c>null</c> se não existe.</summary>
    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    public void Add(RefreshToken token);

    /// <summary>
    /// Consome o token só se ainda estiver utilizável (não consumido, não
    /// revogado, não expirado), gravando <c>ReplacedByTokenId</c>, num único
    /// UPDATE condicional. Devolve <c>false</c> se nenhuma linha foi afetada —
    /// ou seja, outro chamador chegou antes (concorrência) ou o estado mudou.
    /// </summary>
    public Task<bool> TryConsumeAsync(Guid tokenId, Guid replacedByTokenId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Revoga, de imediato, todos os tokens ainda não revogados da sessão.</summary>
    public Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como revogados (no change tracker, <b>sem salvar</b>) todos os
    /// tokens ainda não revogados do usuário — para entrar no mesmo
    /// <c>SaveChangesAsync</c> da troca de senha (RN-AUTH-19, atomicidade).
    /// </summary>
    public Task RevokeAllForUserAsync(Guid userId, RefreshTokenRevocationReason reason, DateTime now, CancellationToken cancellationToken);
}
