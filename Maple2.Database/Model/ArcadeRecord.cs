using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character, arcade game and arcade season: the best score of that game in that season and when it was
// set. The rows go with the character.
internal class ArcadeRecord {
    public long CharacterId { get; set; }
    public int ArcadeId { get; set; }
    public int SeasonId { get; set; }
    public int Score { get; set; }
    public DateTime SetTime { get; set; }

    public static void Configure(EntityTypeBuilder<ArcadeRecord> builder) {
        builder.ToTable("arcade-record");
        builder.HasKey(record => new { record.CharacterId, record.ArcadeId, record.SeasonId });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.ArcadeId, record.SeasonId, record.Score });
    }
}
