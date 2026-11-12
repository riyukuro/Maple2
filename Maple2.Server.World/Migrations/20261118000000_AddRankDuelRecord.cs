using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddRankDuelRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rank-duel-job-record",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    JobCode = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank-duel-job-record", x => new { x.CharacterId, x.JobCode });
                    table.ForeignKey(
                        name: "FK_rank-duel-job-record_character_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "rank-duel-record",
                columns: table => new
                {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    Season = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    BestRating = table.Column<int>(type: "int", nullable: false),
                    WeeklyReward = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rank-duel-record", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_rank-duel-record_character_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rank-duel-job-record");

            migrationBuilder.DropTable(
                name: "rank-duel-record");
        }
    }
}
