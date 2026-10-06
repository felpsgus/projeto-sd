namespace TodoList.Identity.Application.Authentication;

/// <summary>Request de troca de senha (BE-15). <see cref="UserId"/> vem do token já validado pelo Gateway.</summary>
public sealed record ChangePasswordRequest(Guid UserId, string? CurrentPassword, string? NewPassword)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(ChangePasswordRequest);
}
