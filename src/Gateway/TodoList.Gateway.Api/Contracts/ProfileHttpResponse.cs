namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de perfil de usuário (BE-07/BE-14) — resposta de
/// <c>POST /api/auth/register</c> (201), <c>GET /api/me</c> (200) e
/// <c>PATCH /api/me</c> (200). Nunca contém senha nem hash (RN-AUTH-05) — o
/// DTO estruturalmente não tem esse campo.
/// </summary>
public sealed record ProfileHttpResponse(string Id, string Email, string DisplayName, DateTimeOffset CreatedAt);
