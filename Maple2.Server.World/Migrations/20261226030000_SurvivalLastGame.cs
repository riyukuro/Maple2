using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class SurvivalLastGame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The time of a record's last game (Unix seconds), which the survival ranking rows show. The rows stored
            // before get the time of this migration, the latest they can have been played.
            migrationBuilder.AddColumn<long>(
                name: "LastGame",
                table: "survival-record",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql("UPDATE `survival-record` SET `LastGame` = UNIX_TIMESTAMP();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastGame",
                table: "survival-record");
        }
    }
}
