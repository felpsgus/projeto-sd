using TodoList.Identity.Application.Authentication;
using TodoList.Identity.Application.Persistence;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Users;

/// <summary>
/// Altera o nome de exibição do usuário autenticado (BE-14, RN-USER-02).
/// O e-mail é imutável por construção (RN-USER-03): esta mensagem/request
/// nem tem campo de e-mail, então não há como violá-la por aqui. A validação
/// de formato (1–100 caracteres após trim, não só espaços) é a mesma que
/// <see cref="Domain.Users.User.Rename"/> já garante (BE-04) — sem duplicar
/// regra.
/// </summary>
public sealed class UpdateProfileHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public UpdateProfileHandler(IUserRepository userRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ProfileResult>> HandleAsync(Guid userId, string? displayName, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ProfileResult>(AuthErrors.UserNotFound);
        }

        var renameResult = user.Rename(displayName ?? string.Empty, _timeProvider);

        if (renameResult.IsFailure)
        {
            return Result.Failure<ProfileResult>(renameResult.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ProfileResult(user.Id, user.Email.Value, user.DisplayName, user.CreatedAt));
    }
}
