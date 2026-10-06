namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de sucesso de <c>POST /api/auth/login</c> (BE-36, CA-02) — só o
/// access token e a expiração (D-36: sem refresh token, sem cookie).
/// </summary>
public sealed record LoginHttpResponse(string AccessToken, DateTimeOffset ExpiresAt);
