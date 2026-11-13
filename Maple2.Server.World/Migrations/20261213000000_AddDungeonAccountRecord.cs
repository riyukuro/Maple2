using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddDungeonAccountRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dungeon-account-record",
                columns: table => new
                {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    DungeonId = table.Column<int>(type: "int", nullable: false),
                    CurrentSubClears = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CurrentClears = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExtraCurrentSubClears = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExtraCurrentClears = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExtraCooldownTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DailyResetTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UnionCooldownTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dungeon-account-record", x => new { x.AccountId, x.DungeonId });
                    table.ForeignKey(
                        name: "FK_dungeon-account-record_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dungeon-account-record");
        }
    }
}
