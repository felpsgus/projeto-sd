namespace TodoList.SharedKernel.Persistence;

/// <summary>Repete um DELETE em lotes até um lote vir menor que o tamanho (BE-23, CA-07): sem transação gigante.</summary>
public static class BatchDelete
{
    public static async Task<int> RunAsync(Func<CancellationToken, Task<int>> deleteBatch, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            deleted = await deleteBatch(cancellationToken);
            total += deleted;
        }
        while (deleted >= batchSize);

        return total;
    }
}
