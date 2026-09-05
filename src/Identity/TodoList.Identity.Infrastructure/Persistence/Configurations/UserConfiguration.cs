using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TodoList.Identity.Domain.Users;

namespace TodoList.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento EF de <see cref="User"/> (BE-04). Aplicado via
/// <c>ApplyConfigurationsFromAssembly</c> em <see cref="IdentityDbContext.OnModelCreating"/>.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Tamanho generoso o bastante para qualquer algoritmo de hash real (BE-06)
    /// — bcrypt/Argon2/PBKDF2 codificados em Base64 ficam bem abaixo disso.
    /// </summary>
    public const int PasswordHashMaxLength = 512;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        // Id é gerado no domínio (Guid.NewGuid() em User.Create), nunca pelo
        // banco (RN-USER-01) — sem isto o EF assumiria a convenção padrão de
        // Guid gerado no INSERT e poderia sobrescrever um id já atribuído.
        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        // Email é value object convertido de/para string (nota técnica de
        // BE-04). Ler de volta do banco sempre produz um Email válido porque
        // só passamos a persistir valores já validados por Email.Create —
        // .Value aqui nunca lança na prática.
        builder.Property(user => user.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength)
            .IsRequired();

        // RN-AUTH-02: e-mail único no sistema — garantia de banco (CA-10,
        // CA-11 de BE-04), não só a checagem em memória do caso de uso.
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.DisplayName)
            .HasMaxLength(User.DisplayNameMaxLength)
            .IsRequired();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(PasswordHashMaxLength)
            .IsRequired();

        builder.Property(user => user.IsActive)
            .IsRequired();

        builder.Property(user => user.CreatedAt)
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .IsRequired();
    }
}
