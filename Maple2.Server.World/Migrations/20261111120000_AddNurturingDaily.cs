using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class AddNurturingDaily : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.CreateTable(
                name: "nurturing-daily",
                columns: table => new {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<long>(type: "bigint", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Played = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table => {
                    table.PrimaryKey("PK_nurturing-daily", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_nurturing-daily_AccountId",
                table: "nurturing-daily",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropTable(
                name: "nurturing-daily");
        }
    }
}
