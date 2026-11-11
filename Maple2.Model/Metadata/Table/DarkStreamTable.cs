using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

// table/darkstream.xml: the Dark Stream reward by the rounds cleared in a run (round 0 is a run that cleared
// none). The client does not read the table; the server gives the row and sends it in the result.
public record DarkStreamTable(IReadOnlyDictionary<int, DarkStreamTable.Entry> Entries) : Table {
    public record Entry(
        int Round,
        float ExpRate,
        int Meso,
        int Habi,
        IReadOnlyList<RewardItem> Items);
}
