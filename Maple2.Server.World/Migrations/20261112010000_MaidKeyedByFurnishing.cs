using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class MaidKeyedByFurnishing : Migration {
        // A maid row is keyed by the cube uid while its furnishing is placed and by the furnishing storage item uid
        // while it is stored, so it no longer refers to the cube table. The rows there are keyed by placed cubes and
        // stay as they are.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropForeignKey(
                name: "FK_maid_ugcmap-cube_Id",
                table: "maid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            // Stored maids (keyed by an item uid) have no cube to refer to.
            migrationBuilder.Sql("DELETE FROM `maid` WHERE `Id` NOT IN (SELECT `Id` FROM `ugcmap-cube`);");
            migrationBuilder.AddForeignKey(
                name: "FK_maid_ugcmap-cube_Id",
                table: "maid",
                column: "Id",
                principalTable: "ugcmap-cube",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
