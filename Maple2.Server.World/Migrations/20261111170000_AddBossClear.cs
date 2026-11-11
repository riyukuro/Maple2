using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AddBossClear : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "boss-clear",
                columns: table => new {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RankingId = table.Column<int>(type: "int", nullable: false),
                    DungeonId = table.Column<int>(type: "int", nullable: false),
                    LeaderId = table.Column<long>(type: "bigint", nullable: false),
                    Seconds = table.Column<int>(type: "int", nullable: false),
                    ClearTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_boss-clear", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "boss-clear-member",
                columns: table => new {
                    ClearId = table.Column<long>(type: "bigint", nullable: false),
                    CharacterId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_boss-clear-member", x => new { x.ClearId, x.CharacterId });
                    table.ForeignKey(
                        name: "FK_boss-clear-member_boss-clear_ClearId",
                        column: x => x.ClearId,
                        principalTable: "boss-clear",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_boss-clear-member_character_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_boss-clear_RankingId_ClearTime",
                table: "boss-clear",
                columns: new[] { "RankingId", "ClearTime" });

            migrationBuilder.CreateIndex(
                name: "IX_boss-clear_RankingId_Seconds",
                table: "boss-clear",
                columns: new[] { "RankingId", "Seconds" });

            migrationBuilder.CreateIndex(
                name: "IX_boss-clear-member_CharacterId",
                table: "boss-clear-member",
                column: "CharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "boss-clear-member");

            migrationBuilder.DropTable(
                name: "boss-clear");
        }
    }
}
