namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>POST /api/auth/login</c> (BE-36, D-36 — recorte de BE-09: só e-mail e senha).</summary>
public sealed record LoginHttpRequest(string? Email, string? Password);
