using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountSurvivalSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SurvivalSeason",
                table: "account",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SurvivalSeason",
                table: "account");
        }
    }
}
