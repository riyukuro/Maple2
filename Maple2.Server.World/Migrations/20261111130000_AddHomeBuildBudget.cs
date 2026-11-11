using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AddHomeBuildBudget : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<long>(
                name: "BuildMerat",
                table: "home",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "BuildMeso",
                table: "home",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Builders",
                table: "home",
                type: "json",
                defaultValue: "[]",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "BuildMerat",
                table: "home");

            migrationBuilder.DropColumn(
                name: "BuildMeso",
                table: "home");

            migrationBuilder.DropColumn(
                name: "Builders",
                table: "home");
        }
    }
}
