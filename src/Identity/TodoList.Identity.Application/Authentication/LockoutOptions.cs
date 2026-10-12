using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Application.Authentication;

/// <summary>
/// Configuração do bloqueio temporário por tentativas de login (BE-12,
/// RN-AUTH-13, D-03), seção <c>Lockout</c>, validada na inicialização.
/// POCO sem dependência de <c>Microsoft.Extensions.Options</c> — a
/// Application não a referencia; a Infrastructure faz o <c>Bind</c>.
/// </summary>
public sealed class LockoutOptions
{
    public const string SectionName = "Lockout";

    /// <summary>Desliga o bloqueio por completo (CA-12) — útil em teste de carga.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Quantas tentativas são processadas; a seguinte, dentro do bloqueio, é recusada.</summary>
    [Range(1, 100, ErrorMessage = "Lockout:MaxAttempts deve estar entre 1 e 100.")]
    public int MaxAttempts { get; init; } = 5;

    [Range(1, 1440, ErrorMessage = "Lockout:LockoutMinutes deve estar entre 1 e 1440.")]
    public int LockoutMinutes { get; init; } = 15;

    /// <summary>Janela em que as tentativas contam como consecutivas (CA-09).</summary>
    [Range(1, 1440, ErrorMessage = "Lockout:AttemptWindowMinutes deve estar entre 1 e 1440.")]
    public int AttemptWindowMinutes { get; init; } = 15;
}
