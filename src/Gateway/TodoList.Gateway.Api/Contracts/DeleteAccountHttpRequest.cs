namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>DELETE /api/me</c> (BE-16, D-19: confirmação de senha).</summary>
public sealed record DeleteAccountHttpRequest(string? Password)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(DeleteAccountHttpRequest);
}
