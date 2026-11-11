using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character: its best Dark Stream score and when it was set. The row goes with the character.
internal class DarkStreamRecord {
    public long CharacterId { get; set; }
    public int Score { get; set; }
    public DateTime SetTime { get; set; }

    public static void Configure(EntityTypeBuilder<DarkStreamRecord> builder) {
        builder.ToTable("dark-stream-record");
        builder.HasKey(record => record.CharacterId);
        builder.HasOne<Character>()
            .WithOne()
            .HasForeignKey<DarkStreamRecord>(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => record.Score);
    }
}
