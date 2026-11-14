using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// The counter of a promotionContents limit (PromotionTable): how many of its rows' boxes the account (CharacterId 0,
// for a row with isAccount) or one character of the account has taken in the period that ends at ExpirationTime
// (unix seconds). A counter past its end counts from 0 again, so no reset touches the table. ShareId is the rows'
// rewardLimitShareID. Count and ExpirationTime are concurrency tokens: two characters of one account online together
// cannot both take the last box of the account's counter, nor both restart an expired counter (whose count may be the
// same before and after).
internal class PromotionCount {
    public long AccountId { get; set; }
    public long CharacterId { get; set; }
    public int ShareId { get; set; }
    public int Count { get; set; }
    public long ExpirationTime { get; set; }

    public static void Configure(EntityTypeBuilder<PromotionCount> builder) {
        builder.ToTable("promotion-count");
        builder.HasKey(count => new { count.AccountId, count.CharacterId, count.ShareId });
        builder.Property(count => count.Count).IsConcurrencyToken();
        builder.Property(count => count.ExpirationTime).IsConcurrencyToken();
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(count => count.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
