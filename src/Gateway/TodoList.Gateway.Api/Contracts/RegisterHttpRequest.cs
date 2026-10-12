namespace TodoList.Gateway.Api.Contracts;

/// <summary>
/// Corpo JSON de <c>POST /api/auth/register</c> (BE-36/BE-07) — rota anônima.
/// <see cref="DisplayName"/> ausente, nulo ou só espaços é tratado pelo
/// Identity como "não informado" (RN-AUTH-07): o Gateway não duplica essa
/// normalização, só repassa o valor cru.
/// </summary>
public sealed record RegisterHttpRequest(string? Email, string? Password, string? DisplayName)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(RegisterHttpRequest);
}
