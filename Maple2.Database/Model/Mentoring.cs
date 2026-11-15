using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per mentor, mentee and season: the mentor's request to the mentee until the mentee answers, then the pair
// (Accepted). The rows go with either character. A row is the season's (the ReturnUser row's value3): the client's
// accept box names the season's end as the pair's end, so a row of another season is not read. Rows stored before
// the season was kept have season 0, which no season has, until a login of either character moves them to the
// season running then (GameStorage.MoveUnseasonedMentoring) or, without one, to RetiredSeason, which no season reads.
internal class Mentoring {
    public const int RetiredSeason = -1;

    public long Id { get; set; }
    public long MentorId { get; set; } // character id of the mentor
    public long MenteeId { get; set; } // character id of the mentee
    public bool Accepted { get; set; }
    public int Season { get; set; }
    // The mentor coins of a pair: what the mentee earned for its mentor (from its season missions) and the mentor
    // has not taken yet (GameStorage.AddMentorCoins, TakeMentorCoins).
    public long MentorCoins { get; set; }
    public DateTime CreationTime { get; set; }

    public static void Configure(EntityTypeBuilder<Mentoring> builder) {
        builder.ToTable("mentoring");
        builder.HasKey(mentoring => mentoring.Id);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(mentoring => mentoring.MentorId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Character>()
            .WithMany()
            .HasForeignKey(mentoring => mentoring.MenteeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(mentoring => new { mentoring.MentorId, mentoring.MenteeId, mentoring.Season }).IsUnique();
        builder.HasIndex(mentoring => mentoring.MenteeId);
    }
}
