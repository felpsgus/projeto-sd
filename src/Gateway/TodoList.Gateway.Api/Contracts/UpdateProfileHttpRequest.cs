namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de <c>PATCH /api/me</c> (BE-14). De propósito, não tem campo
/// <c>email</c> — RN-USER-03 é garantida por construção: se o cliente enviar
/// <c>email</c> no corpo, a desserialização simplesmente o ignora.
/// </summary>
public sealed record UpdateProfileHttpRequest(string? DisplayName);
