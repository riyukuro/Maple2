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
        // The boards of a season read its rows by rating (GameStorage.RankDuelRanking).
        builder.HasIndex(record => new { record.Season, record.Rating });
    }
}

// One row per character and closed PvP season: the standing the character ended that season with (the rank duel
// boards of the seasons before, irrq.aspx 0x2c and 0x2a). Written when the character's record moves to a new season,
// so a character that has not fought since keeps its closed season in rank-duel-record. The rows go with the
// character.
internal class RankDuelSeasonRecord {
    public long CharacterId { get; set; }
    public int Season { get; set; }
    public int Rating { get; set; }
    public int BestRating { get; set; }

    public static void Configure(EntityTypeBuilder<RankDuelSeasonRecord> builder) {
        builder.ToTable("rank-duel-season");
        builder.HasKey(record => new { record.CharacterId, record.Season });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(record => new { record.Season, record.Rating });
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
