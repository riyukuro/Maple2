using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class UniqueBuddyPair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A pair kept twice on one side (two invites that crossed) keeps one row before the index can be made: a
            // friend row if there is one, else the oldest row, the first committed invite's (each invite adds both
            // sides at once, so both sides keep the same invite).
            migrationBuilder.Sql(
                "DELETE b FROM `buddy` b JOIN `buddy` k ON k.`OwnerId` = b.`OwnerId` AND k.`BuddyId` = b.`BuddyId` " +
                "AND ((k.`Type` = 0 AND b.`Type` <> 0) OR ((k.`Type` = 0) = (b.`Type` = 0) AND k.`Id` < b.`Id`));");

            // The new index is made first: MySQL keeps an index for the OwnerId foreign key at all times.
            migrationBuilder.CreateIndex(
                name: "IX_buddy_OwnerId_BuddyId",
                table: "buddy",
                columns: new[] { "OwnerId", "BuddyId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_buddy_OwnerId",
                table: "buddy");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_buddy_OwnerId",
                table: "buddy",
                column: "OwnerId");

            migrationBuilder.DropIndex(
                name: "IX_buddy_OwnerId_BuddyId",
                table: "buddy");
        }
    }
}
