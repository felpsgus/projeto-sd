using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoList.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "login_attempts",
                schema: "identity",
                columns: table => new
                {
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    failed_count = table.Column<int>(type: "integer", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_login_attempts", x => x.normalized_email);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_attempts",
                schema: "identity");
        }
    }
}
