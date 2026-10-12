using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de remoção de tarefa (BE-21) — RN-TASK-12, RN-TASK-13,
/// RN-TASK-14, RN-AUTZ-02, RN-AUTZ-03. Resolve exclusivamente por
/// <see cref="ITodoTaskRepository.GetOwnedTaskAsync"/> (BE-18) e chama
/// <see cref="Domain.Tasks.TodoTask.SoftDelete"/> (D-07) — nunca
/// <see cref="ITodoTaskRepository.Remove"/>, que seria um <c>DELETE</c> físico.
///
/// <para>
/// <b>Idempotência (BE-21, nota técnica):</b> <c>GetOwnedTaskAsync</c> já
/// filtra por não-removida (BE-18), então uma tarefa já removida nunca chega
/// viva até este handler — o resultado é o mesmo <see cref="TaskErrors.NotFound"/>
/// de uma tarefa inexistente ou alheia, não o <see cref="Domain.Tasks.TodoTaskErrors.AlreadyDeleted"/>
/// do Domain (que existe como guarda de invariante da entidade, mas é
/// inalcançável por este caminho específico — só seria observável se algum
/// código chamasse <c>SoftDelete</c> sobre uma tarefa obtida por um método
/// diferente de <c>GetOwnedTaskAsync</c>, o que BE-18 já proíbe).
/// </para>
/// </summary>
public sealed class DeleteTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public DeleteTaskHandler(
        ITodoTaskRepository repository,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _repository.GetOwnedTaskAsync(_currentUser.Id, taskId, cancellationToken);

        if (task is null)
        {
            return Result.Failure(TaskErrors.NotFound);
        }

        var deleteResult = task.SoftDelete(_timeProvider);

        if (deleteResult.IsFailure)
        {
            return Result.Failure(deleteResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
