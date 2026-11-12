using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character: its rank duel standing (Pvp 22). The rows go with the character.
internal class RankDuelRecord {
    public long CharacterId { get; set; }
    public int Season { get; set; }
    public int Rating { get; set; }
    public int BestRating { get; set; }
    public int WeeklyReward { get; set; }

    public static void Configure(EntityTypeBuilder<RankDuelRecord> builder) {
        builder.ToTable("rank-duel-record");
        builder.HasKey(record => record.CharacterId);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

// One row per character and opponent job code: the season's wins and losses against that job (Pvp 23).
internal class RankDuelJobRecord {
    public long CharacterId { get; set; }
    public int JobCode { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    public static void Configure(EntityTypeBuilder<RankDuelJobRecord> builder) {
        builder.ToTable("rank-duel-job-record");
        builder.HasKey(record => new { record.CharacterId, record.JobCode });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
