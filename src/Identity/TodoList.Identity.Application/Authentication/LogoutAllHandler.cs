using TodoList.Identity.Application.Sessions;
using TodoList.Identity.Domain.Sessions;
using TodoList.SharedKernel;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// "Sair de todos os dispositivos" (BE-11, RN-AUTH-19): revoga todas as sessões
/// do usuário, com motivo Logout.
/// </summary>
public sealed class LogoutAllHandler
{
    private readonly RefreshTokenService _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutAllHandler(RefreshTokenService refreshTokens, IUnitOfWork unitOfWork)
    {
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _refreshTokens.RevokeAllForUserAsync(userId, RefreshTokenRevocationReason.Logout, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
