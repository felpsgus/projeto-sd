using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoList.Tasks.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // BE-02, CA-02: ainda não há nenhuma entidade de negócio (TodoTask
        // é BE-05) — esta migration inicial só garante o schema do
        // serviço. A tabela __EFMigrationsHistory é criada pelo próprio
        // `dotnet ef database update`/`Database.Migrate()` dentro deste
        // mesmo schema (MigrationsHistoryTable("__EFMigrationsHistory",
        // "tasks") em TasksDbContext), então não precisa de DDL explícito
        // aqui — mas o schema precisa existir antes, e como o modelo está
        // vazio o scaffolder não geraria isso sozinho.
        //
        // A FK cruzada tasks.tasks.owner_id -> identity.users(id) ON
        // DELETE CASCADE (D-27, CA-02c/15/16 de BE-02) NÃO entra aqui:
        // nem tasks.tasks nem identity.users existem nesta rodada. Ela
        // chega por SQL explícito numa migration futura deste serviço,
        // assim que BE-04 e BE-05 estiverem prontos (sustenta BE-16).
        migrationBuilder.EnsureSchema(name: "tasks");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS tasks CASCADE;");
    }
}
