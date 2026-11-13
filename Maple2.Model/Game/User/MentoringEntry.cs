namespace Maple2.Model.Game;

// A stored mentoring row: the mentor's request to the mentee until it is accepted, then the pair.
public record MentoringEntry(long Id, long MentorId, long MenteeId, bool Accepted) {
    // The other character of the row, seen from characterId.
    public long PartnerOf(long characterId) => characterId == MentorId ? MenteeId : MentorId;
}
