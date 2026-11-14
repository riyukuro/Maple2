using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class MaidTalkTimes : Migration {
        // The last mood talk and the last affinity talk of a maid (epoch seconds), which maid_mood_time and
        // maid_affinity_time test. Existing maids start at 0: both talks are open.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.AddColumn<long>(
                name: "AffinityTime",
                table: "maid",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "MoodTime",
                table: "maid",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            migrationBuilder.DropColumn(
                name: "AffinityTime",
                table: "maid");

            migrationBuilder.DropColumn(
                name: "MoodTime",
                table: "maid");
        }
    }
}
