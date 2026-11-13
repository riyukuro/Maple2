using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// An account restriction (AdminBlock 0x62). One row per restriction; the client keeps one per kind.
internal class AccountBlock {
    public long Id { get; set; }
    public long AccountId { get; set; }
    public int Kind { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime ExpiryTime { get; set; }
    // The GM account that set it.
    public long IssuerId { get; set; }

    public static void Configure(EntityTypeBuilder<AccountBlock> builder) {
        builder.ToTable("account-block");
        builder.HasKey(block => block.Id);
        builder.Property(block => block.Reason).HasMaxLength(255).IsRequired();
        builder.HasIndex(block => block.AccountId);
    }
}
