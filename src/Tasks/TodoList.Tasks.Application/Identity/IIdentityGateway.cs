namespace TodoList.Tasks.Application.Identity;

/// <summary>
/// Abstração de <c>Application</c> para perguntar ao Identity Service se um
/// usuário existe e está ativo (RN-AUTZ-01, RN-USER-04). A implementação real
/// (<c>GrpcIdentityGateway</c>, em <c>TodoList.Tasks.Infrastructure</c>) fala
/// gRPC; esta interface não sabe disso — é a mesma regra que já vale para
/// <c>DbContext</c> (BE-02, CA-12): trocar o transporte é reescrever uma
/// classe da <c>Infrastructure</c>, não tocar em nenhum caso de uso.
/// </summary>
public interface IIdentityGateway
{
    /// <summary>
    /// Consulta o Identity pelo usuário com o id informado. Nunca lança para
    /// "não existe" ou "inativo" — os dois são resultado de negócio, refletidos
    /// em <see cref="UserValidation"/>. Uma falha de transporte (Identity fora
    /// do ar, deadline excedido) vira <see cref="IdentityUnavailableException"/>,
    /// lançada pela implementação da <c>Infrastructure</c> — nunca uma
    /// <c>Grpc.Core.RpcException</c>, que não deve atravessar essa fronteira.
    /// </summary>
    public Task<UserValidation> ValidateUserAsync(Guid userId, CancellationToken cancellationToken);
}
