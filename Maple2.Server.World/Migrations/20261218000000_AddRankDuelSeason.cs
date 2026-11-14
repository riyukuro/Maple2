using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddRankDuelSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rank-duel-season",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Season = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    BestRating = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank-duel-season", x => new { x.CharacterId, x.Season });
                    table.ForeignKey(
                        name: "FK_rank-duel-season_character_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_rank-duel-season_Season_Rating",
                table: "rank-duel-season",
                columns: new[] { "Season", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_rank-duel-record_Season_Rating",
                table: "rank-duel-record",
                columns: new[] { "Season", "Rating" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_rank-duel-record_Season_Rating",
                table: "rank-duel-record");

            migrationBuilder.DropTable(
                name: "rank-duel-season");
        }
    }
}
