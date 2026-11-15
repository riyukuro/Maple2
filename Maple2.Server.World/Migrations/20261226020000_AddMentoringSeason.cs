using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations
{
    /// <inheritdoc />
    public partial class AddMentoringSeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The rows stored before get season 0, which no season reads; a login of either character moves them to
            // the season running then, or ends them when none runs (GameStorage.MoveUnseasonedMentoring).
            migrationBuilder.AddColumn<int>(
                name: "Season",
                table: "mentoring",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // The mentor coins of a pair, owed by the mentee's season missions until the mentor takes them.
            migrationBuilder.AddColumn<long>(
                name: "MentorCoins",
                table: "mentoring",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // The new index first: the old one is the index of the MentorId foreign key until another starts with
            // MentorId, and MySQL refuses to drop it before.
            migrationBuilder.CreateIndex(
                name: "IX_mentoring_MentorId_MenteeId_Season",
                table: "mentoring",
                columns: new[] { "MentorId", "MenteeId", "Season" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_mentoring_MentorId_MenteeId",
                table: "mentoring");

            // A registered character was its stored role 2 (mentee) or 5 (mentor), and an account could have several;
            // now the account has one, kept as the season's event value MentoringRegistration ("role,character id").
            // The account's registration is kept here under event 0 (no event): its registered character that holds
            // an accepted pair in its role (a mentor's mentee, a mentee's mentor), else its lowest registered
            // character. The first login moves it to the running season or ends it (MentoringManager.MoveUnseasoned).
            migrationBuilder.Sql(
                "INSERT INTO `account-event-value` (`AccountId`, `EventId`, `Type`, `Value`, `ExpirationTime`) "
                + "SELECT r.`AccountId`, 0, 2414, CONCAT(r.`MentorRole`, ',', r.`Id`), 9223372036854775807 FROM ("
                + "SELECT c.`AccountId`, c.`Id`, c.`MentorRole`, ROW_NUMBER() OVER (PARTITION BY c.`AccountId` ORDER BY "
                + "EXISTS (SELECT 1 FROM `mentoring` m WHERE m.`Accepted` AND ((c.`MentorRole` = 5 AND m.`MentorId` = c.`Id`) "
                + "OR (c.`MentorRole` = 2 AND m.`MenteeId` = c.`Id`))) DESC, c.`Id`) AS n "
                + "FROM `character` c WHERE c.`MentorRole` IN (2, 5)) r "
                + "WHERE r.n = 1 AND NOT EXISTS (SELECT 1 FROM `account-event-value` v WHERE v.`AccountId` = r.`AccountId` "
                + "AND v.`EventId` = 0 AND v.`Type` = 2414);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `account-event-value` WHERE `EventId` = 0 AND `Type` = 2414;");

            // Rows of several seasons for one mentor and mentee would break the old unique index: the newest is kept.
            migrationBuilder.Sql("DELETE m FROM `mentoring` m JOIN `mentoring` n ON n.`MentorId` = m.`MentorId` AND n.`MenteeId` = m.`MenteeId` AND n.`Id` > m.`Id`;");

            migrationBuilder.CreateIndex(
                name: "IX_mentoring_MentorId_MenteeId",
                table: "mentoring",
                columns: new[] { "MentorId", "MenteeId" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_mentoring_MentorId_MenteeId_Season",
                table: "mentoring");

            migrationBuilder.DropColumn(
                name: "MentorCoins",
                table: "mentoring");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "mentoring");
        }
    }
}
