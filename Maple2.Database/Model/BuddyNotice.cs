using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// A friend notice for a character that was offline when a friend request it sent was accepted or declined, or when a
// friend removed it. The rows are sent at the character's next login and then deleted; they go with the character.
internal class BuddyNotice {
    public long Id { get; set; }
    public long OwnerId { get; set; }
    public required string Name { get; set; }
    public BuddyNoticeType Type { get; set; }
    public long EntryId { get; set; }

    public static void Configure(EntityTypeBuilder<BuddyNotice> builder) {
        builder.ToTable("buddy-notice");
        builder.HasKey(notice => notice.Id);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(notice => notice.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
