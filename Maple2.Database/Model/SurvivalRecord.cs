using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per character and Maple Survival season (the period id of maplesurvivalopenperiod): the season rating
// and the games, wins and kills. The rows go with the character.
internal class SurvivalRecord {
    public long CharacterId { get; set; }
    public int SeasonId { get; set; }
    public int Rating { get; set; }
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Kills { get; set; }

    public static void Configure(EntityTypeBuilder<SurvivalRecord> builder) {
        builder.ToTable("survival-record");
        builder.HasKey(record => new { record.CharacterId, record.SeasonId });
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(record => record.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
