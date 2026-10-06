namespace TodoList.Gateway.Api.Contracts;

/// <summary>Corpo JSON de <c>POST /api/auth/login</c> (BE-36, D-36 — recorte de BE-09: só e-mail e senha).</summary>
public sealed record LoginHttpRequest(string? Email, string? Password)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(LoginHttpRequest);
}
