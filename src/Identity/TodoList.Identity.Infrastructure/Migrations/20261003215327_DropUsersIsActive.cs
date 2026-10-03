using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TodoList.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropUsersIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "identity",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }
    }
}
