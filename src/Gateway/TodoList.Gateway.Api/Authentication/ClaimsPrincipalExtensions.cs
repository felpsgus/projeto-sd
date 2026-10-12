using System.Security.Claims;

namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Extrai o <c>user_id</c> a enviar ao Identity nos RPCs de perfil/conta
/// (BE-14/BE-15/BE-16) a partir do claim <c>sub</c> do token já validado pelo
/// Gateway (D-38) — nunca do corpo ou da query da requisição HTTP. Usado por
/// <c>Endpoints.UserEndpoints</c>, o mesmo mecanismo que
/// <see cref="Backends.ClientMetadataInterceptor"/> já usa para preencher
/// <c>x-user-id</c> no cliente do Tasks; aqui o valor vai direto num campo da
/// mensagem proto (<c>GetProfileRequest.UserId</c> e análogos), não num
/// header, porque o contrato do Identity carrega <c>user_id</c> explicitamente
/// (BE-25).
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// O claim <c>sub</c> nunca está ausente aqui: todo endpoint que chama
    /// isto está sob a fallback policy (<c>RequireAuthenticatedUser</c>), que
    /// já rejeitou a requisição com 401 antes do handler rodar caso o token
    /// não tivesse o claim.
    /// </summary>
    public static string GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst(JwtClaimTypes.Subject)?.Value;

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                "Claim 'sub' ausente numa requisição autenticada — a fallback policy deveria ter rejeitado isto antes do handler.");
        }

        return value;
    }
}
