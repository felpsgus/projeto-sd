namespace TodoList.Gateway.Api.Authentication;

/// <summary>Constantes do esquema de autenticação do Gateway (BE-36).</summary>
public static class IdentityAuthenticationDefaults
{
    /// <summary>Nome do esquema registrado para <see cref="IdentityTokenAuthenticationHandler"/>.</summary>
    public const string SchemeName = "IdentityToken";
}

/// <summary>Nomes de claim usados pelo <see cref="IdentityTokenAuthenticationHandler"/> (BE-36).</summary>
public static class IdentityClaimTypes
{
    /// <summary>Mesmo nome de claim que o Identity usa para o id do usuário (BE-08) — <c>sub</c>.</summary>
    public const string Subject = "sub";
}
