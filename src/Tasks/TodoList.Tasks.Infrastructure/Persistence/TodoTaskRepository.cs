using Microsoft.EntityFrameworkCore;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Tasks;
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
    /// <summary>
    /// Caractere de escape usado no <c>LIKE</c> da busca textual (BE-22,
    /// CA-15) — precisa ser escapado antes de qualquer outro caractere
    /// especial, senão um <c>\</c> literal no termo de busca vira parte de
    /// uma sequência de escape indevida.
    /// </summary>
    private const string LikeEscapeCharacter = "\\";

    private readonly TasksDbContext _context;

    public TodoTaskRepository(TasksDbContext context)
    {
        _context = context;
    }

    public void Add(TodoTask task) => _context.Tasks.Add(task);

    public void Remove(TodoTask task) => _context.Tasks.Remove(task);

    public Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        Query().CountAsync(task => task.OwnerId == ownerId && task.Status == TodoTaskStatus.Pending, cancellationToken);

    private DbSet<TodoTask> Query() => _context.Tasks;

    public async Task<(IReadOnlyList<TodoTask> Items, int TotalCount)> ListByOwnerAsync(
        Guid ownerId, int page, int pageSize, TaskListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // RN-LIST-01: filtro base inegociável — dono corrente e não removida
        // (o soft delete já vem do filtro global de query, ver TasksDbContext).
        var query = Query().Where(task => task.OwnerId == ownerId);

        query = ApplyStatusFilter(query, filter.Status);
        query = ApplyPriorityFilter(query, filter.Priorities);
        query = ApplyOverdueFilter(query, filter.Overdue, filter.Today);
        query = ApplySearchFilter(query, filter.Search);

        // CA-25: total_count reflete o total APÓS os filtros — uma consulta
        // própria, sem Skip/Take, sobre a mesma query já filtrada acima.
        var totalCount = await query.CountAsync(cancellationToken);

        // RN-LIST-06, nesta ordem exata: (1) pendentes antes de concluídas —
        // Status == Completed vale false (0) para Pending e true (1) para
        // Completed, então ordenar por essa expressão ascendente já põe
        // Pending primeiro; (2) vencimento crescente com as sem vencimento
        // por último — a chave auxiliar "DueDate == null" (0 para quem tem
        // vencimento, 1 para quem não tem) separa os dois grupos antes de
        // ordenar pelo valor em si, o equivalente explícito a
        // "ORDER BY due_date ASC NULLS LAST" sem depender do comportamento
        // default de NULL de um dialeto específico; (3) criação crescente;
        // (4) Id como desempate final, para a paginação nunca repetir nem
        // pular itens entre páginas (CA-22, CA-27).
        var items = await query
            .OrderBy(task => task.Status == TodoTaskStatus.Completed)
            .ThenBy(task => task.DueDate == null)
            .ThenBy(task => task.DueDate)
            .ThenBy(task => task.CreatedAt)
            .ThenBy(task => task.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<TodoTask?> GetOwnedTaskAsync(Guid ownerId, Guid taskId, CancellationToken cancellationToken = default) =>
        Query().SingleOrDefaultAsync(task => task.OwnerId == ownerId && task.Id == taskId, cancellationToken);

    /// <summary>RN-LIST-02: status=All (padrão) não filtra; pending/completed filtram no WHERE.</summary>
    private static IQueryable<TodoTask> ApplyStatusFilter(IQueryable<TodoTask> query, TaskStatusFilter status) => status switch
    {
        TaskStatusFilter.Pending => query.Where(task => task.Status == TodoTaskStatus.Pending),
        TaskStatusFilter.Completed => query.Where(task => task.Status == TodoTaskStatus.Completed),
        _ => query,
    };

    /// <summary>RN-LIST-03: lista vazia não filtra; do contrário, "priority IN (...)" no WHERE.</summary>
    private static IQueryable<TodoTask> ApplyPriorityFilter(IQueryable<TodoTask> query, IReadOnlyList<TaskPriority> priorities) =>
        priorities.Count == 0 ? query : query.Where(task => priorities.Contains(task.Priority));

    /// <summary>
    /// RN-LIST-04/RN-TASK-16: "atrasada" é <b>derivada</b>, nunca uma coluna
    /// — o predicado "status=Pending AND DueDate &lt; today" é composto aqui
    /// dentro do próprio <see cref="IQueryable{T}"/> (CA-33), então vira parte
    /// do mesmo <c>WHERE</c> traduzido para SQL, nunca avaliado em memória.
    /// <paramref name="today"/> é o mesmo valor que a projeção de cada item
    /// usa para <c>isOverdue</c> (ver <see cref="ListTasksHandler"/>, CA-33c).
    /// </summary>
    private static IQueryable<TodoTask> ApplyOverdueFilter(IQueryable<TodoTask> query, bool? overdue, DateOnly today)
    {
        if (overdue is null)
        {
            return query;
        }

        return overdue.Value
            ? query.Where(task => task.Status == TodoTaskStatus.Pending && task.DueDate != null && task.DueDate < today)
            : query.Where(task => !(task.Status == TodoTaskStatus.Pending && task.DueDate != null && task.DueDate < today));
    }

    /// <summary>
    /// RN-LIST-05/D-16: busca em título e descrição, case-insensitive (via
    /// <c>ToLower()</c> dos dois lados — portável entre Postgres e o SQLite
    /// usado pelos testes gRPC, ao contrário de uma função específica de um
    /// dialeto), com os curingas de <c>LIKE</c> escapados antes de compor o
    /// padrão (CA-15).
    /// </summary>
    private static IQueryable<TodoTask> ApplySearchFilter(IQueryable<TodoTask> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var pattern = "%" + EscapeLikeWildcards(search.Trim().ToLowerInvariant()) + "%";

        return query.Where(task =>
            EF.Functions.Like(task.Title.ToLower(), pattern, LikeEscapeCharacter) ||
            (task.Description != null && EF.Functions.Like(task.Description.ToLower(), pattern, LikeEscapeCharacter)));
    }

    /// <summary>
    /// Escapa <c>\</c>, <c>%</c> e <c>_</c> (nesta ordem — o próprio caractere
    /// de escape precisa ser escapado primeiro) para que um termo de busca
    /// contendo esses caracteres seja tratado como texto literal, nunca como
    /// curinga de <c>LIKE</c> (BE-22, CA-15).
    /// </summary>
    private static string EscapeLikeWildcards(string value) =>
        value
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);
}
