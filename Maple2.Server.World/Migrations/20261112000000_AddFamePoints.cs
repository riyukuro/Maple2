using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AddFamePoints : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<string>(
                name: "FamePoints",
                table: "character-unlock",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "FamePoints",
                table: "character-unlock");
        }
    }
}
