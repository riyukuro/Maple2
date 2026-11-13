using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// A guild expel for a character that was offline when it was expelled. The rows are sent at the character's next login
// (Guild 76) and then deleted; they go with the character.
internal class GuildExpelNotice {
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public required string Name { get; set; }
    public long Time { get; set; }

    public static void Configure(EntityTypeBuilder<GuildExpelNotice> builder) {
        builder.ToTable("guild-expel-notice");
        builder.HasKey(notice => notice.Id);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(notice => notice.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
