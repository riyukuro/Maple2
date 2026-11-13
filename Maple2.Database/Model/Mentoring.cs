using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Model;

// One row per mentor and mentee: the mentor's request to the mentee until the mentee answers, then the pair
// (Accepted). The rows go with either character.
internal class Mentoring {
    public long Id { get; set; }
    public long MentorId { get; set; } // character id of the mentor
    public long MenteeId { get; set; } // character id of the mentee
    public bool Accepted { get; set; }
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
        builder.HasIndex(mentoring => new { mentoring.MentorId, mentoring.MenteeId }).IsUnique();
        builder.HasIndex(mentoring => mentoring.MenteeId);
    }
}
