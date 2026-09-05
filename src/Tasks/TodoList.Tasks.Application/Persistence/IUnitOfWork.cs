namespace TodoList.Tasks.Application.Persistence;

/// <summary>
/// Abstração de persistência exposta à Application (BE-02, CA-12): apenas
/// <see cref="SaveChangesAsync"/> — a Application não referencia
/// <c>Microsoft.EntityFrameworkCore</c> nem <c>DbContext</c> concreto, só esta
/// interface. A Infrastructure implementa isso no próprio
/// <c>TasksDbContext</c> (<c>TasksDbContext : DbContext, IUnitOfWork</c>), já
/// que <c>DbContext.SaveChangesAsync(CancellationToken)</c> tem exatamente
/// esta assinatura — não precisa de wrapper.
/// </summary>
public interface IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
