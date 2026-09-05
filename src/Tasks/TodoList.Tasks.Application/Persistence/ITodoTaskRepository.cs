using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Persistence;

/// <summary>
/// Acesso a <see cref="TodoTask"/> visto pela <c>Application</c> (BE-05). Só
/// <see cref="IQueryable{T}"/> (System.Linq, BCL) — nenhum tipo de
/// <c>Microsoft.EntityFrameworkCore</c> atravessa esta fronteira (CA-12 de
/// BE-02). Confirmar/persistir uma mudança é sempre um passo à parte, por
/// <see cref="IUnitOfWork.SaveChangesAsync"/> — nenhum método aqui commita
/// sozinho.
///
/// <para>
/// Implementação concreta fica na <c>Infrastructure</c> (BE-05); os casos de
/// uso que efetivamente chamam estes métodos chegam só em BE-17 a BE-22 —
/// esta interface é o contrato que eles vão consumir.
/// </para>
/// </summary>
public interface ITodoTaskRepository
{
    /// <summary>
    /// Busca por id respeitando o filtro global de soft delete (BE-02,
    /// CA-06) — uma tarefa removida não é encontrada por aqui.
    /// </summary>
    public Task<TodoTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Passa a rastrear <paramref name="task"/> como nova — só é persistida no próximo <see cref="IUnitOfWork.SaveChangesAsync"/>.</summary>
    public void Add(TodoTask task);

    /// <summary>
    /// Remove <paramref name="task"/>. Como <see cref="TodoTask"/> implementa
    /// <see cref="Domain.Common.ISoftDeletable"/>, o interceptor de auditoria
    /// da Infrastructure converte isso automaticamente numa remoção lógica ao
    /// salvar (BE-02) — nenhum <c>DELETE</c> físico sai daqui. Reservado para
    /// o fluxo de remoção "genérico"; o expurgo definitivo (BE-23, fora do
    /// escopo desta task) usa outro caminho.
    /// </summary>
    public void Remove(TodoTask task);

    /// <summary>
    /// Conta as tarefas <i>ativas</i> do dono — <c>Pending</c> e não
    /// removidas (RN-TASK-15: base do limite de 500, que é regra do caso de
    /// uso, não do domínio — ver BE-17).
    /// </summary>
    public Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta composável (filtro, ordenação, paginação) para os casos de
    /// uso de listagem (BE-22). Já respeita o filtro global de soft delete.
    /// </summary>
    public IQueryable<TodoTask> Query();
}
