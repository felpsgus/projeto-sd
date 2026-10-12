using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TodoList.SharedKernel.Persistence;
using TodoList.SharedKernel.Retention;
using TodoList.Tasks.Application.Tasks;
using TodoList.Tasks.Infrastructure.Persistence;

namespace TodoList.Tasks.Infrastructure.Retention;

/// <summary>
/// Apaga fisicamente tarefas removidas há mais de <c>Tasks:SoftDeleteRetentionDays</c> (BE-23, RN-TASK-13, D-12).
/// Junto com BE-16, o único uso autorizado de <c>IgnoreQueryFilters()</c> sobre <c>TodoTask</c>.
/// </summary>
public sealed class TasksRetentionPurger : IRetentionPurger
{
    private readonly TasksDbContext _context;
    private readonly TimeProvider _time;
    private readonly int _retentionDays;
    private readonly int _batchSize;

    public TasksRetentionPurger(
        TasksDbContext context, TimeProvider time, IOptions<TaskOptions> tasks, IOptions<RetentionOptions> retention)
    {
        _context = context;
        _time = time;
        _retentionDays = tasks.Value.SoftDeleteRetentionDays;
        _batchSize = retention.Value.BatchSize;
    }

    public async Task<IReadOnlyDictionary<string, int>> PurgeAsync(CancellationToken cancellationToken)
    {
        var cutoff = _time.GetUtcNow().UtcDateTime.AddDays(-_retentionDays);

        // DeletedAt nulo (tarefa ativa) nunca satisfaz "< cutoff" (CA-03).
        var removed = await BatchDelete.RunAsync(
            ct =>
            {
                var batch = _context.Tasks.IgnoreQueryFilters()
                    .Where(task => task.DeletedAt < cutoff)
                    .OrderBy(task => task.DeletedAt)
                    .Take(_batchSize)
                    .Select(task => task.Id);

                return _context.Tasks.IgnoreQueryFilters()
                    .Where(task => batch.Contains(task.Id))
                    .ExecuteDeleteAsync(ct);
            },
            _batchSize,
            cancellationToken);

        return new Dictionary<string, int> { ["tasks"] = removed };
    }
}
