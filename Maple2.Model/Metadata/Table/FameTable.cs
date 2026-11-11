using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// table/famepickmethod.xml and table/famedef.xml, as CFameTable reads them (famegradedata.cpp:571, :604): how the
// server picks the alliance missions (alliance quests, type 9) a player may take each day or week, and which NPC kind
// is the officer of which alliance.
public record FameTable(
    IReadOnlyList<FameTable.PickMethod> PickMethods,
    IReadOnlyDictionary<int, AllianceType> OfficerNpcKinds) : Table {
    // One method: the quests of this repeat (2 daily, 3 weekly) are picked from the listed alliances, by rank (S, A, B,
    // C, D) the counts of PickCountByRank. Combination (RankAllianceCombination) picks each rank's count from all the
    // listed alliances together, each alliance at most its MaxCount quests; otherwise (FixedCountPerAlliance) each
    // alliance gets each rank's count. ReloadOnComplete is the quest after whose completion the picks are made again.
    public record PickMethod(
        bool Combination,
        int Repeat,
        int[] PickCountByRank,
        int ReloadOnAccept,
        int ReloadOnComplete,
        IReadOnlyList<Alliance> Alliances);

    // MaxCount 0: no limit.
    public record Alliance(AllianceType Type, int MaxCount);
}
