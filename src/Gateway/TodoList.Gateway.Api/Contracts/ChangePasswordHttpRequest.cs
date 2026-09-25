namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>POST /api/me/change-password</c> (BE-15).</summary>
public sealed record ChangePasswordHttpRequest(string? CurrentPassword, string? NewPassword);
