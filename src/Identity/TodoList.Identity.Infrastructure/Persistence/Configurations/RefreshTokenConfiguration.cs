using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoList.Identity.Domain.Sessions;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Mapeamento EF de <see cref="RefreshToken"/> (BE-10), tabela <c>identity.refresh_tokens</c>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>SHA-256 em hex minúsculo = 64 caracteres.</summary>
    public const int TokenHashLength = 64;

    /// <summary>Comporta o nome de qualquer <see cref="RefreshTokenRevocationReason"/>.</summary>
    public const int RevokedReasonMaxLength = 32;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedNever();

        // RN-AUTH-20: só o hash; o índice único é o que torna a busca por
        // token O(1) e impede dois tokens com o mesmo valor (CA-20).
        builder.Property(token => token.TokenHash)
            .HasMaxLength(TokenHashLength)
            .IsRequired();

        builder.HasIndex(token => token.TokenHash).IsUnique();

        builder.Property(token => token.UserId).IsRequired();
        builder.Property(token => token.SessionId).IsRequired();

        // Revogação por sessão e por usuário percorre este índice.
        builder.HasIndex(token => new { token.UserId, token.SessionId });

        builder.Property(token => token.ExpiresAt).IsRequired();
        builder.Property(token => token.CreatedAt).IsRequired();

        builder.Property(token => token.RevokedReason)
            .HasConversion<string>()
            .HasMaxLength(RevokedReasonMaxLength);

        // BE-16/RN-AUTH-19: excluir o usuário leva os tokens junto, no mesmo
        // DELETE — nenhum código de aplicação na exclusão de conta.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
