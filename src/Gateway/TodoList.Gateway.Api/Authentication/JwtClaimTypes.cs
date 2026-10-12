namespace TodoList.Gateway.Api.Authentication;

/// <summary>
/// Nomes de claim usados após a validação local do JWT (BE-40, D-38) —
/// substitui a antiga <c>IdentityClaimTypes</c>, removida com o esquema
/// <c>IdentityToken</c>. <see cref="Subject"/> é o mesmo claim <c>sub</c> que
/// o Identity emite (BE-08); <see cref="Backends.ClientMetadataInterceptor"/>
/// lê-o para preencher <c>x-user-id</c> (D-34), a partir do
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> que o
/// <c>AddJwtBearer</c> monta com <c>MapInboundClaims=false</c> (sem isso, o
/// claim viraria o URI longo de <c>ClaimTypes.NameIdentifier</c> e o
/// interceptor pararia de encontrá-lo em silêncio).
/// </summary>
public static class JwtClaimTypes
{
    public const string Subject = "sub";
}
