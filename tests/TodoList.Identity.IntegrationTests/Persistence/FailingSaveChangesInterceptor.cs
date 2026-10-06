using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TodoList.Identity.IntegrationTests.Persistence;

/// <summary>
/// Falha de persistência sob demanda (BE-15 CA-11/12, BE-16 CA-11): enquanto
/// <see cref="Armed"/>, todo <c>SaveChangesAsync</c> do host sob teste lança, então
/// nada do que o handler fez no mesmo save pode ter sido gravado.
/// </summary>
internal sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        Armed ? throw new InvalidOperationException("Falha de persistência forçada pelo teste.") : base.SavingChangesAsync(eventData, result, cancellationToken);
}
