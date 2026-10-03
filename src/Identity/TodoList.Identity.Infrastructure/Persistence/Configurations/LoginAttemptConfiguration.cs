using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoList.Identity.Domain.Users;
using TodoList.Identity.Infrastructure.Authentication;

namespace TodoList.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Mapeamento EF de <see cref="LoginAttempt"/> (BE-12), tabela <c>identity.login_attempts</c>.</summary>
public sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts");

        // A chave é o e-mail normalizado: o upsert atômico depende do ON CONFLICT nela.
        builder.HasKey(attempt => attempt.NormalizedEmail);

        builder.Property(attempt => attempt.NormalizedEmail)
            .HasMaxLength(Email.MaxLength)
            .ValueGeneratedNever();

        builder.Property(attempt => attempt.FailedCount).IsRequired();
        builder.Property(attempt => attempt.LastAttemptAt).IsRequired();
    }
}
