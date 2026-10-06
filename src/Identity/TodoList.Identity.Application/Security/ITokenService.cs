using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Application.Security;

/// <summary>
/// Emissão de access token JWT (BE-08, RN-AUTH-10/RN-AUTH-11). A
/// implementação real (<c>JwtTokenService</c>) vive na Infrastructure — a
/// Application não conhece <c>Microsoft.IdentityModel.JsonWebTokens</c>, só
/// este contrato.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Gera um access token de vida curta para <paramref name="user"/>. O
    /// token carrega só <c>sub</c> (id), <c>email</c> (normalizado),
    /// <c>jti</c> (novo a cada chamada), <c>iat</c>, <c>exp</c>, <c>iss</c> e
    /// <c>aud</c> — nenhum outro dado do usuário (CA-04/CA-05).
    /// </summary>
    public AccessToken GenerateAccessToken(User user);
}
