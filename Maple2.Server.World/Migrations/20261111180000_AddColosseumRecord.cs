using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AddColosseumRecord : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "colosseum-record",
                columns: table => new {
                    CharacterId = table.Column<long>(type: "bigint", nullable: false),
                    SeasonId = table.Column<int>(type: "int", nullable: false),
                    Rounds = table.Column<int>(type: "int", nullable: false),
                    Seconds = table.Column<int>(type: "int", nullable: false),
                    SetTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_colosseum-record", x => new { x.CharacterId, x.SeasonId });
                    table.ForeignKey(
                        name: "FK_colosseum-record_character_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "character",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_colosseum-record_SeasonId_Rounds",
                table: "colosseum-record",
                columns: new[] { "SeasonId", "Rounds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "colosseum-record");
        }
    }
}
