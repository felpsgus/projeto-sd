namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>DELETE /api/me</c> (BE-16, D-19: confirmação de senha).</summary>
public sealed record DeleteAccountHttpRequest(string? Password);
