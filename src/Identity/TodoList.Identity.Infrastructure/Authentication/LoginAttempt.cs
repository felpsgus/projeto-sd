namespace TodoList.Identity.Infrastructure.Authentication;

/// <summary>
/// Linha de <c>identity.login_attempts</c> (BE-12): contador por e-mail normalizado.
/// Não é entidade de domínio nem tem FK para <c>users</c> — e-mails inexistentes
/// também são contados (CA-08, ADR 0002). Escrita só por <see cref="LoginAttemptStore"/> (SQL atômico).
/// </summary>
public sealed class LoginAttempt
{
    public string NormalizedEmail { get; set; } = string.Empty;

    public int FailedCount { get; set; }

    public DateTime LastAttemptAt { get; set; }

    public DateTime? LockedUntil { get; set; }
}
