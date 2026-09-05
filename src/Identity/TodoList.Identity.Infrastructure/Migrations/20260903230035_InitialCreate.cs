using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoList.Identity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // BE-02, CA-02: ainda não há nenhuma entidade de negócio (User é
        // BE-04) — esta migration inicial só garante o schema do serviço.
        // A tabela __EFMigrationsHistory é criada pelo próprio
        // `dotnet ef database update`/`Database.Migrate()` dentro deste
        // mesmo schema (MigrationsHistoryTable("__EFMigrationsHistory",
        // "identity") em IdentityDbContext), então não precisa de DDL
        // explícito aqui — mas o schema precisa existir antes, e como o
        // modelo está vazio o scaffolder não geraria isso sozinho.
        migrationBuilder.EnsureSchema(name: "identity");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS identity CASCADE;");
    }
}
