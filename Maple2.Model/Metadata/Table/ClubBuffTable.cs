namespace Maple2.Model.Metadata;

// table/clubbuff.xml: the buffs a club leader can pick in the club window, each an additional effect and
// its level.
public record ClubBuffTable(IReadOnlyDictionary<int, ClubBuffTable.Entry> Entries) : Table {
    public record Entry(
        int Id,
        int AdditionalEffectId,
        int AdditionalEffectLevel);
}
