using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class MaidCraft : Migration {
        // The running craft of a maid (MaidCraftItem): the recipe (0 for none), its start time in epoch seconds and
        // its lead time in seconds. Existing maids craft nothing.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<int>(
                name: "CraftDuration",
                table: "maid",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CraftRecipeId",
                table: "maid",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CraftStartTime",
                table: "maid",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "CraftDuration",
                table: "maid");

            migrationBuilder.DropColumn(
                name: "CraftRecipeId",
                table: "maid");

            migrationBuilder.DropColumn(
                name: "CraftStartTime",
                table: "maid");
        }
    }
}
