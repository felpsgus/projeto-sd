using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TodoList.SharedKernel;
using TodoList.Tasks.Application.Errors;
using TodoList.Tasks.Application.Identity;
using TodoList.Tasks.Application.Persistence;
using TodoList.Tasks.Application.Security;
using TodoList.Tasks.Domain.Tasks;

namespace TodoList.Tasks.Application.Tasks;

/// <summary>
/// Caso de uso de criação de tarefa (BE-17, BE-28) — RN-TASK-10, RN-TASK-15,
/// RN-AUTZ-01, RN-USER-04. A ordem dos passos é o contrato da task, e é
/// verificada por teste com ordem de invocação (BE-17 CA-23, BE-28 CA-02/CA-03):
/// <list type="number">
/// <item>o request já chegou validado por FluentValidation antes deste
/// handler ser chamado — o filtro de endpoint (BE-03,
/// <c>ValidationFilter&lt;CreateTaskRequest&gt;</c>) roda antes de qualquer
/// código deste caso de uso, então uma requisição inválida nunca gasta uma
/// chamada gRPC (BE-28 CA-12);</item>
/// <item>valida o dono no Identity via <see cref="IIdentityGateway.ValidateUserAsync"/> —
/// uma única chamada por criação;</item>
/// <item>conta o limite de tarefas ativas do usuário (RN-TASK-15);</item>
/// <item>cria via <see cref="TodoTask.Create"/>, com <c>OwnerId</c> vindo de
/// <see cref="ICurrentUser.Id"/> — este handler não sabe se essa identidade
/// veio de um token ou do header provisório de BE-29;</item>
/// <item>persiste.</item>
/// </list>
/// </summary>
public sealed partial class CreateTaskHandler
{
    private readonly ITodoTaskRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IIdentityGateway _identityGateway;
    private readonly ICurrentUser _currentUser;
    private readonly IClientDate _clientDate;
    private readonly IOptions<TaskOptions> _taskOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateTaskHandler> _logger;

    public CreateTaskHandler(
        ITodoTaskRepository repository,
        IUnitOfWork unitOfWork,
        IIdentityGateway identityGateway,
        ICurrentUser currentUser,
        IClientDate clientDate,
        IOptions<TaskOptions> taskOptions,
        TimeProvider timeProvider,
        ILogger<CreateTaskHandler> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _identityGateway = identityGateway;
        _currentUser = currentUser;
        _clientDate = clientDate;
        _taskOptions = taskOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<TaskResponse>> HandleAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.Id;

        UserValidation validation;
        try
        {
            // Passo 2 (BE-17/BE-28): a única chamada gRPC desta criação — antes
            // da contagem do limite e antes de qualquer escrita no banco.
            validation = await _identityGateway.ValidateUserAsync(ownerId, cancellationToken);
        }
        catch (IdentityUnavailableException exception)
        {
            // D-28: fail-closed. Nenhum detalhe de transporte (endereço,
            // StatusCode gRPC, mensagem da RpcException) sai daqui — só o
            // Error genérico do catálogo, e o detalhe fica só no log (server-side).
            Log.IdentityUnavailable(_logger, ownerId, exception);
            return Result.Failure<TaskResponse>(TaskErrors.IdentityUnavailable);
        }

        if (!validation.Exists)
        {
            Log.OwnerRejected(_logger, ownerId, "not_found");
            return Result.Failure<TaskResponse>(TaskErrors.OwnerNotFound);
        }

        if (!validation.Active)
        {
            Log.OwnerRejected(_logger, ownerId, "inactive");
            return Result.Failure<TaskResponse>(TaskErrors.OwnerInactive);
        }

        // Passo 3: limite de tarefas ativas (RN-TASK-15) — só depois de saber
        // que o dono existe; null desativa o limite (D-08).
        var maxActive = _taskOptions.Value.MaxActivePerUser;
        if (maxActive is not null)
        {
            var activeCount = await _repository.CountActiveByOwnerAsync(ownerId, cancellationToken);
            if (activeCount >= maxActive.Value)
            {
                return Result.Failure<TaskResponse>(TaskErrors.ActiveLimitReached(maxActive.Value));
            }
        }

        // Passo 4: o Domain garante a invariante mesmo que o request já
        // tenha passado pelo FluentValidation (duas camadas, de propósito —
        // ver o comentário de BE-17).
        var createResult = TodoTask.Create(
            ownerId, request.Title, request.Description, request.Priority, request.DueDate, _timeProvider);

        if (createResult.IsFailure)
        {
            return Result.Failure<TaskResponse>(createResult.Error);
        }

        var task = createResult.Value;

        // Passo 5: persiste.
        _repository.Add(task);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(task.ToResponse(_clientDate.Today));
    }

    private static partial class Log
    {
        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Criação de tarefa rejeitada: dono {OwnerId} inválido ({Reason}).")]
        public static partial void OwnerRejected(ILogger logger, Guid ownerId, string reason);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Criação de tarefa abortada: Identity indisponível ao validar o dono {OwnerId}.")]
        public static partial void IdentityUnavailable(ILogger logger, Guid ownerId, Exception exception);
    }
}
