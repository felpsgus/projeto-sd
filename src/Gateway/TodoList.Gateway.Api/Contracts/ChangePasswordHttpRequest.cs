namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>POST /api/me/change-password</c> (BE-15).</summary>
public sealed record ChangePasswordHttpRequest(string? CurrentPassword, string? NewPassword)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(ChangePasswordHttpRequest);
}
