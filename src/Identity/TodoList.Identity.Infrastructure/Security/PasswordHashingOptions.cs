using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Infrastructure.Security;

/// <summary>
/// Custo do PBKDF2-SHA256 usado por <see cref="Pbkdf2PasswordHasher"/> (BE-06),
/// vinculado com <c>IOptions&lt;T&gt;</c> e validado na inicialização — mesmo
/// padrão de <c>ServiceOptions</c>/<c>UserStoreOptions</c> (BE-01/BE-26).
/// </summary>
public sealed class PasswordHashingOptions
{
    public const string SectionName = "PasswordHashing";

    /// <summary>
    /// Recomendação OWASP para PBKDF2-HMAC-SHA256 (2024+). Alterar este valor
    /// não invalida hashes já gerados (CA-05): cada hash guarda as próprias
    /// iterações usadas na criação.
    /// </summary>
    public const int DefaultIterations = 600_000;

    [Range(1, int.MaxValue)]
    public int Iterations { get; init; } = DefaultIterations;
}
