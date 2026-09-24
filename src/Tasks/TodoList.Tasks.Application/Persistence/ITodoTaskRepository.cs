using TodoList.Tasks.Application.Tasks;
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

    /// <summary>
    /// Página de tarefas do dono, não removidas, filtradas por
    /// <paramref name="filter"/> e ordenadas pelo critério fixo de RN-LIST-06
    /// (BE-22): pendentes antes de concluídas, depois vencimento crescente
    /// (sem vencimento por último), depois criação crescente, com desempate
    /// final por <see cref="TodoTask.Id"/> — a mesma ordenação sempre, sem
    /// parâmetro de ordenação alternativa. <paramref name="page"/> é 1-based;
    /// a implementação aplica <c>WHERE</c> (filtro base + filtros +
    /// busca)/<c>ORDER BY</c>/<c>LIMIT</c>/<c>OFFSET</c> inteiramente no banco
    /// (CA-32) — nunca materializa mais linhas que <paramref name="pageSize"/>
    /// além da contagem (CA-34), e o filtro <c>overdue</c> nunca é avaliado em
    /// memória (CA-33). <see cref="ListByOwnerAsync"/> não valida
    /// <paramref name="page"/>/<paramref name="pageSize"/>/<paramref name="filter"/>:
    /// essa validação é do caso de uso (<c>ListTasksHandler</c>), que só chega
    /// até aqui com valores já dentro da faixa permitida.
    /// </summary>
    public Task<(IReadOnlyList<TodoTask> Items, int TotalCount)> ListByOwnerAsync(
        Guid ownerId, int page, int pageSize, TaskListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Único método de resolução de uma tarefa específica por id (BE-41,
    /// recorte de BE-18) — filtra <b>sempre</b> por <paramref name="ownerId"/>
    /// e por não-removida na própria query (RN-AUTZ-02). Tarefa inexistente,
    /// de outro dono ou removida devolvem <c>null</c> indistintamente; quem
    /// traduz isso em <c>TaskErrors.NotFound</c> é o caso de uso
    /// (<c>GetTaskHandler</c>), não este método. Não existe (e não deve
    /// existir) outro método público de leitura por id sem o filtro de dono.
    /// </summary>
    public Task<TodoTask?> GetOwnedTaskAsync(Guid ownerId, Guid taskId, CancellationToken cancellationToken = default);
}
