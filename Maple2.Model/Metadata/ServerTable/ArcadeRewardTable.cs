namespace Maple2.Model.Metadata;

// table/Server/arcadeReward.xml: <arcadeGame arcadeId><reward round expRate meso [locale]/></arcadeGame>, the reward
// of an arcade game by the rounds cleared (round 0 is a game that cleared none). A row of this locale replaces the
// row without a locale for the same round. Entries: arcade id -> round -> row.
public record ArcadeRewardTable(IReadOnlyDictionary<int, IReadOnlyDictionary<int, ArcadeRewardTable.Entry>> Entries) : ServerTable {
    public record Entry(int Round, float ExpRate, int Meso);

    // The row of the rounds cleared, or of the most rounds below them when the table has no row for them.
    public Entry? Of(int arcadeId, int roundsCleared) {
        if (!Entries.TryGetValue(arcadeId, out IReadOnlyDictionary<int, Entry>? rounds)) {
            return null;
        }
        return rounds.Values.Where(row => row.Round <= roundsCleared).MaxBy(row => row.Round);
    }
}
