namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Request de cadastro (BE-07). <paramref name="DisplayName"/> ausente, nulo
/// ou só espaços recebe o mesmo tratamento — <see cref="Domain.Users.User.Create(Domain.Users.Email,string?,string,TimeProvider)"/>
/// usa a parte do e-mail antes do "@" (RN-AUTH-07).
/// </summary>
public sealed record RegisterUserRequest(string? Email, string? Password, string? DisplayName)
{
    // O ToString gerado imprimiria a senha em texto puro (BE-06 CA-08).
    public override string ToString() => nameof(RegisterUserRequest);
}
