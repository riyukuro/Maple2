using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Maple2.Server.World.Migrations {
    /// <inheritdoc />
    public partial class MailExpiryBackfill : Migration {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) {
            migrationBuilder.Sql(Maple2.Database.Extensions.MailExpiryBackfill.Sql());
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) {
            // The mails that had no expiry are not known any more; nothing to undo.
        }
    }
}
