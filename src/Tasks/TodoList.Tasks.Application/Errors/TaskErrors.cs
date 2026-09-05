using TodoList.SharedKernel;
using TodoList.Tasks.Application.Tasks;

namespace TodoList.Tasks.Application.Errors;

/// <summary>
/// Catálogo de erros de negócio do Tasks Service (BE-03, CA-07) — nenhuma
/// mensagem de erro de negócio deve existir fora daqui, e o catálogo não é
/// compartilhado com o Identity (D-26): só o tipo <see cref="Error"/> é comum.
/// </summary>
public static class TaskErrors
{
    /// <summary>
    /// O Identity Service está inalcançável ao validar o dono da tarefa
    /// (D-28, fail-closed). Registrado nesta etapa (BE-03) como dívida
    /// deixada pelo BE-27 — o caso de uso que a consome, capturando
    /// <see cref="TodoList.Tasks.Application.Identity.IdentityUnavailableException"/>
    /// e convertendo-a neste <see cref="Error"/>, é o BE-28.
    /// </summary>
    public static readonly Error IdentityUnavailable = new(
        "identity.unavailable",
        "Não foi possível validar o usuário no momento. Tente novamente em instantes.",
        ErrorType.Unavailable);

    /// <summary>
    /// O Identity respondeu <c>Exists=false</c> para o dono da tarefa
    /// (BE-28, RN-AUTZ-01). Não é <c>400</c>: o dono não vem do corpo do
    /// request — é a identidade de quem chamou —, então isso é recurso
    /// ausente, não payload malformado (nota técnica de BE-28).
    /// </summary>
    public static readonly Error OwnerNotFound = new(
        "task.owner_not_found",
        "O usuário informado não foi encontrado.",
        ErrorType.NotFound);

    /// <summary>
    /// O Identity respondeu <c>Exists=true, Active=false</c> para o dono da
    /// tarefa (BE-28, RN-USER-04). <c>409</c>, não <c>403</c>: a identidade é
    /// válida e o pedido é legítimo — o que impede é o estado atual do
    /// usuário (nota técnica de BE-28).
    /// </summary>
    public static readonly Error OwnerInactive = new(
        "task.owner_inactive",
        "O usuário informado está inativo e não pode criar tarefas.",
        ErrorType.Conflict);

    /// <summary>
    /// RN-TASK-15: usuário já no limite de tarefas ativas. O limite entra na
    /// mensagem (CA-15 de BE-17) — por isso é um método, não um
    /// <c>Error</c> estático fixo: o valor vem de <see cref="TaskOptions.MaxActivePerUser"/>,
    /// configurável (D-08), não uma constante do catálogo.
    /// </summary>
    public static Error ActiveLimitReached(int limit) => new(
        "task.active_limit_reached",
        $"Você atingiu o limite de {limit} tarefas ativas. Conclua ou remova alguma tarefa antes de criar uma nova.",
        ErrorType.Conflict);
}
