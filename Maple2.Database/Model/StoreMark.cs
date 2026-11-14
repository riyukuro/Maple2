using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// The mark of the account's last purchase save (GameSession's purchase save): a new random value written in the
// save's transaction. A commit that throws may have been kept or not (its answer lost with the connection); the mark
// read again afterwards tells which, so a purchase that was stored is not taken for a failed one and refunded.
internal class StoreMark {
    public long AccountId { get; set; }
    public long Mark { get; set; }

    public static void Configure(EntityTypeBuilder<StoreMark> builder) {
        builder.ToTable("store-mark");
        builder.HasKey(mark => mark.AccountId);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(mark => mark.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
