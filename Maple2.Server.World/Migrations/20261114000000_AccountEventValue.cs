using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AccountEventValue : Migration {
        // The game event user values kept for the account instead of a character (a stamp card's claims): they
        // stay when a character is deleted.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "account-event-value",
                columns: table => new {
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    EventId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpirationTime = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_account-event-value", x => new { x.AccountId, x.EventId, x.Type });
                    table.ForeignKey(
                        name: "FK_account-event-value_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "account-event-value");
        }
    }
}
