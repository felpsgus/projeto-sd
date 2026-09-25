using TodoList.Identity.Application.Persistence;
using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Users;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Caso de uso de troca de senha (BE-15, RN-AUTH-04/RN-AUTH-21). Exige a
/// senha atual (impede que um access token roubado, válido por até 15 min,
/// vire posse permanente da conta), valida a política de BE-06 na nova senha
/// e rejeita nova senha igual à atual.
///
/// <para>
/// <b>Recorte da Fase 4 (D-omitido; ver relatório da task).</b> RN-AUTH-19
/// pede revogar todos os refresh tokens do usuário com
/// <c>RevokedReason = PasswordChanged</c> depois da troca — a tabela
/// <c>refresh_tokens</c> não existe nesta fase (BE-10/BE-11 são Fase 4), então
/// esse passo fica de fora aqui, deliberadamente. Quando BE-10/BE-11
/// existirem, a revogação entra **dentro** do mesmo <see cref="IUnitOfWork.SaveChangesAsync"/>
/// já usado abaixo, preservando a atomicidade "senha muda e sessões caem
/// juntas, ou nada acontece" que a task pede.
/// </para>
/// </summary>
public sealed class ChangePasswordHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly TimeProvider _timeProvider;

    public ChangePasswordHandler(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        TimeProvider timeProvider)
    {
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

        // Fase 4 (BE-10/BE-11, RN-AUTH-19): aqui entraria a revogação de todos
        // os refresh tokens do usuário (RevokedReason.PasswordChanged), no
        // mesmo SaveChangesAsync — sem tabela refresh_tokens ainda, não há
        // sessão para revogar.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
