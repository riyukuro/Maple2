using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddWeddingProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Profile",
                table: "marriage",
                newName: "Partner2Profile");

            migrationBuilder.AddColumn<string>(
                name: "Partner1Profile",
                table: "marriage",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Partner1Profile",
                table: "marriage");

            migrationBuilder.RenameColumn(
                name: "Partner2Profile",
                table: "marriage",
                newName: "Profile");
        }
    }
}
