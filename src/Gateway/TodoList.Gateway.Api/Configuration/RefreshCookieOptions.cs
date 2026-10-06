namespace TodoList.Gateway.Api.Configuration;

/// <summary>
/// Seção <c>RefreshCookie</c> do Gateway (BE-09/BE-10, D-20/D-42): atributos do
/// cookie <c>refreshToken</c> que variam por ambiente. <c>HttpOnly</c>,
/// <c>SameSite=Strict</c> e <c>Path=/api/auth</c> são fixos no código
/// (<see cref="Http.RefreshCookie"/>) — só <see cref="Secure"/> é configuração.
/// </summary>
public sealed class RefreshCookieOptions
{
    public const string SectionName = "RefreshCookie";

    /// <summary>
    /// Atributo <c>Secure</c> do cookie. Padrão <c>true</c>: um cookie de
    /// credencial não deve trafegar em texto claro. O navegador descarta cookie
    /// <c>Secure</c> recebido por HTTP puro num host que não é <c>localhost</c>,
    /// então um deploy HTTP-only (a VM da demo, por IP) precisa de
    /// <c>RefreshCookie__Secure=false</c> — escolha explícita, nunca o padrão.
    /// </summary>
    public bool Secure { get; init; } = true;
}
