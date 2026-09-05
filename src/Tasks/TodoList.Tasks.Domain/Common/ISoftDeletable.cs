namespace TodoList.Tasks.Domain.Common;

/// <summary>
/// Entidade com remoção lógica (RN-TASK-13/D-07, BE-02 CA-06):
/// <see cref="DeletedAt"/> nulo significa ativa. O <c>DbContext</c> aplica um
/// <c>HasQueryFilter</c> global excluindo os removidos das consultas normais;
/// <c>IgnoreQueryFilters()</c> é o caminho explícito, usado só pelo expurgo
/// (BE-23) e por auditoria.
/// </summary>
public interface ISoftDeletable
{
    public DateTime? DeletedAt { get; set; }
}
