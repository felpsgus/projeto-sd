using TodoList.Identity.Application.Users;

namespace TodoList.Identity.Infrastructure.Users;

/// <summary>
/// Implementação de <see cref="IUserLookup"/> sobre o banco real
/// (<see cref="IUserRepository"/>/<c>IdentityDbContext</c>) — o caminho
/// preferido agora que BE-04 está pronto (BE-26, CA-13). Seleção via
/// <c>UserStore:Provider = "Persisted"</c>.
///
/// <para>
/// <b>Sem cache, sem estado guardado entre chamadas.</b> Cada
/// <see cref="FindByIdAsync"/> é uma consulta nova ao <see cref="IUserRepository"/>
/// injetado, que por sua vez é <c>Scoped</c> sobre o <c>DbContext</c> da
/// requisição — não há nada memorizado entre chamadas que pudesse ficar
/// desatualizado. É isso que garante que desativar um usuário no banco muda
/// a resposta de <c>ValidateUser</c> de <c>active=true</c> para
/// <c>active=false</c> sem reiniciar o serviço (CA-13): a próxima chamada
/// simplesmente lê o estado atual.
/// </para>
/// </summary>
public sealed class PersistedUserLookup : IUserLookup
{
    private readonly IUserRepository _userRepository;

    public PersistedUserLookup(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserLookupResult?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        return user is null ? null : new UserLookupResult(user.IsActive, user.DisplayName);
    }
}
