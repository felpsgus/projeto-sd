using TodoList.Identity.Application.Authentication;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Users;

/// <summary>
/// Consulta o perfil do usuário autenticado (BE-14, RN-USER-01). Opera
/// sempre sobre o <c>user_id</c> recebido (o <c>sub</c> do token, já
/// validado pelo Gateway) — não existe parâmetro alternativo, então não há
/// como um usuário consultar o perfil de outro (CA-04).
/// </summary>
public sealed class GetProfileHandler
{
    private readonly IUserRepository _userRepository;

    public GetProfileHandler(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Result<ProfileResult>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ProfileResult>(AuthErrors.UserNotFound);
        }

        return Result.Success(new ProfileResult(user.Id, user.Email.Value, user.DisplayName, user.CreatedAt));
    }
}
