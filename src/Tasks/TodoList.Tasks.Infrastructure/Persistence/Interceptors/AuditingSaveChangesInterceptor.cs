using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TodoList.Tasks.Domain.Common;

namespace TodoList.Tasks.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Interceptor único de auditoria + soft delete (BE-02, CA-06/CA-07/CA-08).
/// Preenche <c>CreatedAt</c>/<c>UpdatedAt</c> de toda entidade
/// <see cref="IAuditable"/> a cada <c>SaveChanges(Async)</c>, e converte a
/// remoção física de uma entidade <see cref="ISoftDeletable"/> num
/// <c>UPDATE</c> que só marca <c>DeletedAt</c> — quem chama
/// <c>DbSet.Remove(...)</c> não precisa saber disso. O tempo vem sempre do
/// <see cref="TimeProvider"/> injetado (CA-08), nunca de
/// <see cref="DateTime.UtcNow"/> direto — é o que permite avançar um
/// <c>FakeTimeProvider</c> em teste sem <c>Thread.Sleep</c>.
/// </summary>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;

    public AuditingSaveChangesInterceptor(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditingAndSoftDelete(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditingAndSoftDelete(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditingAndSoftDelete(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Soft delete primeiro: uma entidade removida também passa a contar
        // como "alterada" para fins de UpdatedAt, no laço seguinte.
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.DeletedAt = now;
            }
        }

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
