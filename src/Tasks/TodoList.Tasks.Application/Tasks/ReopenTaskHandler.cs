using Microsoft.Extensions.Options;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de reabertura de tarefa (BE-20) — RN-TASK-09, RN-TASK-14,
/// RN-TASK-15, RN-AUTZ-02, RN-AUTZ-03. Mesmo desenho de
/// <see cref="CompleteTaskHandler"/>, espelhado: resolve por
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/> e repassa a transição
/// para <see cref="Domain.Tasks.TodoTask.Reopen"/> (BE-05). Reabrir uma
/// tarefa que já está Pending é <see cref="Domain.Tasks.TodoTaskErrors.NotCompleted"/>
/// (Conflict/409).
///
/// <para>
/// <b>Decisão do cliente (23/09/2026) sobre o limite de tarefas ativas
/// (RN-TASK-15) e reabertura:</b> o limite vale para o <i>total</i> de
/// tarefas ativas do usuário em qualquer momento, não apenas para o ato de
/// criar. Reabrir uma tarefa <c>Completed</c> a torna <c>Pending</c> — e
/// "ativa" (RN-TASK-15) é "não concluída e não removida" (nota técnica de
/// BE-17) — logo reabrir aumenta a contagem de ativas em um exatamente como
/// criar aumenta. Por isso este handler verifica o limite antes de reabrir,
/// reaproveitando o mesmo mecanismo de <see cref="CreateTaskHandler"/>: a
/// mesma leitura de <see cref="TaskOptions.MaxActivePerUser"/>, a mesma
/// contagem via <see cref="ITodoTaskRepository.CountActiveByOwnerAsync"/> e o
/// mesmo <see cref="TaskErrors.ActiveLimitReached"/> do catálogo — sem
/// duplicar constante, mensagem ou lógica. Se o usuário já está no limite, a
/// reabertura falha em vez de deixar o total ultrapassar o teto.
/// </para>
/// </summary>
public sealed class ReopenTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;
    private readonly IOptions<TaskOptions> _taskOptions;
    private readonly TimeProvider _timeProvider;

    public ReopenTaskHandler(
        ITodoTaskRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClientDate clientDate,
        IOptions<TaskOptions> taskOptions,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clientDate = clientDate;
        _taskOptions = taskOptions;
        _timeProvider = timeProvider;
    }

    public async Task<Result<TaskResponse>> HandleAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.Id;
        var task = await _repository.GetOwnedTaskAsync(ownerId, taskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<TaskResponse>(TaskErrors.NotFound);
        }

        // RN-TASK-15 (decisão de 23/09/2026): o limite vale para o total de
        // ativas, não só para criações — mesmo mecanismo de CreateTaskHandler.
        var maxActive = _taskOptions.Value.MaxActivePerUser;
        if (maxActive is not null)
        {
            var activeCount = await _repository.CountActiveByOwnerAsync(ownerId, cancellationToken);
            if (activeCount >= maxActive.Value)
            {
                return Result.Failure<TaskResponse>(TaskErrors.ActiveLimitReached(maxActive.Value));
            }
        }

        var reopenResult = task.Reopen(_timeProvider);

        if (reopenResult.IsFailure)
        {
            return Result.Failure<TaskResponse>(reopenResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(task.ToResponse(_clientDate.Today));
    }
}
