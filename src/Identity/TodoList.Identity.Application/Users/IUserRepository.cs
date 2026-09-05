using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Application.Users;

/// <summary>
/// Repositório de <see cref="User"/> (BE-04) — a Application só conhece esta
/// abstração, nunca <c>DbContext</c>/EF Core diretamente (mesma regra de
/// <c>IUnitOfWork</c>, BE-02 CA-12). A implementação real
/// (<c>UserRepository</c>) vive em <c>TodoList.Identity.Infrastructure</c>.
///
/// <para>
/// <see cref="Add"/> e <see cref="Remove"/> são síncronos de propósito: só
/// marcam a mudança no rastreador do EF, a persistência de fato acontece em
/// <c>IUnitOfWork.SaveChangesAsync</c> — quem chama decide quando comitar,
/// permitindo agrupar mais de uma alteração numa única transação.
/// </para>
/// </summary>
public interface IUserRepository
{
    /// <summary>Busca por id. Retorna <c>null</c> se nenhum usuário existe com esse id — nunca lança para "não encontrado".</summary>
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Busca pelo e-mail já normalizado (<see cref="Email"/> normaliza na
    /// própria criação — RN-AUTH-02/RN-AUTH-03). Retorna <c>null</c> se nenhum
    /// usuário tem esse e-mail.
    /// </summary>
    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// Checagem de unicidade em memória (mensagem amigável no caso de uso) —
    /// a garantia definitiva sob concorrência é o índice único no banco (nota
    /// técnica de BE-04); esta consulta não substitui aquele índice.
    /// </summary>
    public Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken);

    public void Add(User user);

    public void Remove(User user);
}
