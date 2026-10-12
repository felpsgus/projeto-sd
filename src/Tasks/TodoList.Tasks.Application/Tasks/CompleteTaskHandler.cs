using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de conclusão de tarefa (BE-20) — RN-TASK-08, RN-TASK-14,
/// RN-AUTZ-02, RN-AUTZ-03. Resolve exclusivamente por
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/> (BE-18) e repassa a
/// transição para <see cref="Domain.Tasks.TodoTask.Complete"/> (BE-05) — este
/// handler não reimplementa a regra "só uma tarefa Pending pode ser
/// concluída"; ele só traduz o <see cref="Result"/> que o Domain já devolve.
/// Concluir uma tarefa já concluída é <see cref="Domain.Tasks.TodoTaskErrors.AlreadyCompleted"/>
/// (Conflict/409, D-35) — idempotência deliberada, não um 200 silencioso
/// (BE-20, nota técnica).
/// </summary>
public sealed class CompleteTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;
    private readonly TimeProvider _timeProvider;

    public CompleteTaskHandler(
        ITodoTaskRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IClientDate clientDate,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clientDate = clientDate;
        _timeProvider = timeProvider;
    }

    public async Task<Result<TaskResponse>> HandleAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _repository.GetOwnedTaskAsync(_currentUser.Id, taskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure<TaskResponse>(TaskErrors.NotFound);
        }

        var completeResult = task.Complete(_timeProvider);

        if (completeResult.IsFailure)
        {
            return Result.Failure<TaskResponse>(completeResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(task.ToResponse(_clientDate.Today));
    }
}
