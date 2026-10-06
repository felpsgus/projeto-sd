using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Sessions;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Caso de uso de troca de senha (BE-15, RN-AUTH-04/RN-AUTH-21). Exige a
/// senha atual (impede que um access token roubado, válido por até 15 min,
/// vire posse permanente da conta), valida a política de BE-06 na nova senha
/// e rejeita nova senha igual à atual.
///
/// <para>
/// <b>RN-AUTH-19 (BE-10).</b> A troca revoga todos os refresh tokens do usuário
/// (<see cref="RefreshTokenRevocationReason.PasswordChanged"/>) no <b>mesmo</b>
/// <see cref="IUnitOfWork.SaveChangesAsync"/> que grava o novo hash: senha muda
/// e sessões caem juntas, ou nada acontece. O access token já emitido continua
/// válido até expirar (D-41).
/// </para>
/// </summary>
public sealed class ChangePasswordHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;
    private readonly RefreshTokenService _refreshTokens;

    public ChangePasswordHandler(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider,
        RefreshTokenService refreshTokens)
    {
        _refreshTokens = refreshTokens;
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _timeProvider = timeProvider;
    }

    public async Task<Result> HandleAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result.Failure(AuthErrors.UserNotFound);
        }

        if (!_passwordHasher.Verify(request.CurrentPassword ?? string.Empty, user.PasswordHash))
        {
            // CA-04: hash não é alterado — nada foi persistido ainda.
            return Result.Failure(AuthErrors.InvalidCurrentPassword);
        }

        var passwordErrors = PasswordPolicy.Validate(request.NewPassword);

        if (passwordErrors.Count > 0)
        {
            return Result.Failure(passwordErrors[0]);
        }

        if (_passwordHasher.Verify(request.NewPassword!, user.PasswordHash))
        {
            // CA-06: nova senha igual à atual.
            return Result.Failure(AuthErrors.NewPasswordSameAsCurrent);
        }

        user.ChangePasswordHash(_passwordHasher.Hash(request.NewPassword!), _timeProvider);

        await _refreshTokens.RevokeAllForUserAsync(user.Id, RefreshTokenRevocationReason.PasswordChanged, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
