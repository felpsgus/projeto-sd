namespace TodoList.SharedKernel.Retention;

/// <summary>
/// Um ciclo de expurgo (BE-23). Cada serviço implementa o seu, sobre o próprio schema
/// (Identity e Tasks não se referenciam); o <c>DataRetentionWorker</c> é o mesmo nos dois.
/// </summary>
public interface IRetentionPurger
{
    /// <summary>Apaga o que passou da retenção; devolve quantos registros foram removidos por tipo.</summary>
    public Task<IReadOnlyDictionary<string, int>> PurgeAsync(CancellationToken cancellationToken);
}
