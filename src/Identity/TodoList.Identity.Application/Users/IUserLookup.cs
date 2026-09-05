namespace TodoList.Identity.Application.Users;

/// <summary>
/// Consulta de usuário pelo id, usada por <c>IdentityGrpcService.ValidateUser</c>
/// (BE-26). Duas implementações possíveis, escolhidas por configuração:
/// persistida sobre o <c>IdentityDbContext</c> (quando BE-04/BE-07 estiverem
/// prontos) ou seed em memória (enquanto não estiverem). A Application não
/// conhece qual das duas está em uso.
/// </summary>
public interface IUserLookup
{
    /// <summary>
    /// Retorna os dados do usuário com o id informado, ou <c>null</c> se
    /// nenhum usuário com esse id existe. Nunca lança para "não encontrado" —
    /// isso é resultado de negócio, não falha (ver CA-07/CA-08 de BE-26).
    /// </summary>
    public Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>
/// Os únicos três dados de usuário que atravessam a fronteira gRPC
/// (<c>ValidateUserResponse</c>) — nunca e-mail, hash de senha ou qualquer
/// outro campo (nota técnica de BE-26).
/// </summary>
/// <param name="Active">Estado atual do usuário (RN-USER-01, RN-USER-04).</param>
/// <param name="DisplayName">Nome de exibição do dono (RN-AUTH-07).</param>
public sealed record UserLookupResult(bool Active, string DisplayName);
