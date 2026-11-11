using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character and Colosseum season: its best run of that season (rounds, seconds) and when it was set.
// The rows go with the character.
internal class ColosseumRecord {
    public long CharacterId { get; set; }
    public int SeasonId { get; set; }
    public int Rounds { get; set; }
    public int Seconds { get; set; }
    public DateTime SetTime { get; set; }

    public static void Configure(EntityTypeBuilder<ColosseumRecord> builder) {
        builder.ToTable("colosseum-record");
        builder.HasKey(record => new { record.CharacterId, record.SeasonId });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.SeasonId, record.Rounds });
    }
}
