using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per player an account keeps out of its home.
internal class HomeBlock {
    public long Id { get; set; }
    public long OwnerId { get; set; } // account id of the home's owner
    public long CharacterId { get; set; } // the character that was picked
    public long AccountId { get; set; } // its account: the block holds for every character of it
    public string Memo { get; set; } = string.Empty;

    public static void Configure(EntityTypeBuilder<HomeBlock> builder) {
        builder.ToTable("home-block");
        builder.HasKey(block => block.Id);
        builder.Property(block => block.Memo).HasMaxLength(255).IsRequired();
        builder.HasIndex(block => new { block.OwnerId, block.AccountId }).IsUnique();
    }
}
