using TodoList.Identity.Application.Security;
using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Application.Users;
using TodoList.Identity.Domain.Sessions;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Renovação de sessão (BE-10, RN-AUTH-14/16/17/18): troca um refresh token
/// válido por um novo par (access + refresh). Toda falha — inexistente,
/// expirado, revogado, reuso, usuário inativo — é o mesmo
/// <see cref="AuthErrors.InvalidRefreshToken"/>.
/// </summary>
public sealed class RefreshSessionHandler
{
    private readonly RefreshTokenService _refreshTokens;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;

    public RefreshSessionHandler(RefreshTokenService refreshTokens, IUserRepository userRepository, ITokenService tokenService)
    {
        _refreshTokens = refreshTokens;
        _userRepository = userRepository;
        _tokenService = tokenService;
    }

    public async Task<Result<RefreshSessionResult>> HandleAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var redeemed = await _refreshTokens.RedeemAsync(refreshToken, cancellationToken);

        if (redeemed.IsFailure)
        {
            return Result.Failure<RefreshSessionResult>(redeemed.Error);
        }

        var user = await _userRepository.GetByIdAsync(redeemed.Value.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            // Conta desativada depois do login (RN-USER-04/RN-AUTH-19): encerra a sessão que acabou de rotacionar.
            await _refreshTokens.RevokeSessionAsync(
                redeemed.Value.Next.SessionId, RefreshTokenRevocationReason.AccountDeactivated, cancellationToken);

            return Result.Failure<RefreshSessionResult>(AuthErrors.InvalidRefreshToken);
        }

        var accessToken = _tokenService.GenerateAccessToken(user);

        return Result.Success(new RefreshSessionResult(accessToken, redeemed.Value.Next));
    }
}

/// <summary>Sucesso de <see cref="RefreshSessionHandler.HandleAsync"/>.</summary>
public sealed record RefreshSessionResult(AccessToken AccessToken, IssuedRefreshToken RefreshToken);
