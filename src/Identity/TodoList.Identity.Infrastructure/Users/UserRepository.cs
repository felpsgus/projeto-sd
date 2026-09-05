using Microsoft.EntityFrameworkCore;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Persistence;

namespace TodoList.Identity.Infrastructure.Users;

/// <summary>
/// Implementação real de <see cref="IUserRepository"/> (BE-04), sobre o
/// <see cref="IdentityDbContext"/>. Registrado como <c>Scoped</c> (mesmo
/// tempo de vida do <c>DbContext</c>, nota técnica de BE-02) — nunca
/// <c>Singleton</c>, o que criaria uma dependência cativa sobre um
/// <c>DbContext</c> descartado.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _context;

    public UserRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken) =>
        _context.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<bool> EmailExistsAsync(Email email, CancellationToken cancellationToken) =>
        _context.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public void Add(User user) => _context.Users.Add(user);

    public void Remove(User user) => _context.Users.Remove(user);
}
