using Microsoft.EntityFrameworkCore;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Infrastructure.Persistence;

/// <summary>
/// Implementação EF de <see cref="ITodoTaskRepository"/> (BE-05) sobre
/// <see cref="TasksDbContext"/>. Nenhum método aqui chama
/// <c>SaveChangesAsync</c> — isso é responsabilidade de quem chama, através
/// de <see cref="TodoList.Tasks.Application.Persistence.IUnitOfWork"/> (o
/// próprio <see cref="TasksDbContext"/>, ver BE-02).
/// </summary>
public sealed class TodoTaskRepository : ITodoTaskRepository
{
    private readonly TasksDbContext _context;

    public TodoTaskRepository(TasksDbContext context)
    {
        _context = context;
    }

    public Task<TodoTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Query().SingleOrDefaultAsync(task => task.Id == id, cancellationToken);

    public void Add(TodoTask task) => _context.Tasks.Add(task);

    public void Remove(TodoTask task) => _context.Tasks.Remove(task);

    public Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        Query().CountAsync(task => task.OwnerId == ownerId && task.Status == TodoTaskStatus.Pending, cancellationToken);

    public IQueryable<TodoTask> Query() => _context.Tasks;
}
