using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record RewardContentTable(IReadOnlyDictionary<int, RewardContentTable.Base> BaseEntries,
                                 IReadOnlyDictionary<int, RewardContentTable.Item> ItemEntries,
                                 IReadOnlyDictionary<int, long> MesoStaticEntries,
                                 IReadOnlyDictionary<int, Dictionary<int, long>> MesoEntries,
                                 IReadOnlyDictionary<int, long> ExpStaticEntries) : Table {
    public record Base(
        int Id,
        int MesoTableId,
        int ExpTableId,
        float MesoFactor,
        float ExpFactor,
        int ItemTableId,
        int PrestigeExpTableId);

    public record Item(
        int Id,
        Item.Data[] ItemData) {

        public record Data(
            int MinLevel,
            int MaxLevel,
            RewardItem[] RewardItems);
    }

    public record Meso(
        int Id,
        IReadOnlyDictionary<int, long> Table); // Level, Meso

    // The meso of a reward contents row at a level (RewardContentsTable::GetReward, GetMeso, rewardcontentstable.cpp:213,
    // 338): the row's meso table, the static one (a single value) at any level, else the level's value, times the row's
    // mesoFactor. No row, no table or a level the table lacks gives 0. The client keeps one table of an id, the level
    // tables' (read first).
    public long RewardMeso(int rewardId, int level) {
        if (!BaseEntries.TryGetValue(rewardId, out Base? row)) {
            return 0;
        }
        long meso;
        if (MesoEntries.TryGetValue(row.MesoTableId, out Dictionary<int, long>? table)) {
            if (level <= 0 || !table.TryGetValue(level, out meso)) {
                return 0;
            }
        } else if (!MesoStaticEntries.TryGetValue(row.MesoTableId, out meso)) {
            return 0;
        }
        return (long) (meso * (double) row.MesoFactor + 0.00001);
    }
}
