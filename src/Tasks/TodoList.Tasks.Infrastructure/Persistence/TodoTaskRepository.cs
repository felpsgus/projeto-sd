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

    public async Task<(IReadOnlyList<TodoTask> Items, int TotalCount)> ListByOwnerAsync(
        Guid ownerId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var ownedQuery = Query().Where(task => task.OwnerId == ownerId);

        // CA-08: total_count é o total do dono (não removidas), não o total
        // da página — uma consulta própria, sem Skip/Take.
        var totalCount = await ownedQuery.CountAsync(cancellationToken);

        // CA-05, CA-09, CA-11: ORDER BY (criação decrescente, desempate por
        // Id) + LIMIT/OFFSET no banco — nunca em memória.
        var items = await ownedQuery
            .OrderByDescending(task => task.CreatedAt)
            .ThenByDescending(task => task.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<TodoTask?> GetOwnedTaskAsync(Guid ownerId, Guid taskId, CancellationToken cancellationToken = default) =>
        Query().SingleOrDefaultAsync(task => task.OwnerId == ownerId && task.Id == taskId, cancellationToken);
}
