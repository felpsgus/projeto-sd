using System.ComponentModel.DataAnnotations;

namespace TodoList.Identity.Infrastructure.Retention;

/// <summary>Seção <c>Auth</c> (BE-23): quanto tempo um refresh token vencido/revogado fica no banco.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(1, 3650, ErrorMessage = "Auth:TokenRetentionDays deve estar entre 1 e 3650.")]
    public int TokenRetentionDays { get; init; } = 30;
}
