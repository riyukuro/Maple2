using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// table/famepickmethod.xml and table/famedef.xml, as CFameTable reads them (famegradedata.cpp:571, :604): how the
// server picks the alliance missions (alliance quests, type 9) a player may take each day or week, and which NPC kind
// is the officer of which alliance. table/fame.xml and table/famelog.xml (LoadFame, LoadFameLog): the points each
// alliance grade needs and the fame board's log lines, each of one alliance and grade.
public record FameTable(
    IReadOnlyList<FameTable.PickMethod> PickMethods,
    IReadOnlyDictionary<int, AllianceType> OfficerNpcKinds,
    IReadOnlyList<FameTable.Grade>? GradeRows = null,
    IReadOnlyList<FameTable.Log>? LogRows = null,
    IReadOnlyDictionary<int, int>? SeasonRequireQuests = null) : Table {
    // Empty in a table stored before the rows were read.
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Grade> Grades => GradeRows ?? [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<Log> Logs => LogRows ?? [];

    // table/famedef.xml <season requireQuest> by season (1 SkyFotress, 2 KritiasExped, 3 Georg; Parse_14021a480,
    // commonenum.cpp:743), as GetSeasonRequireLvBySeason (famegradedata.cpp:855) reads it: 0 for a season the table
    // lacks. Null in a table stored before the seasons were read.
    public int SeasonRequireQuest(int season) =>
        SeasonRequireQuests != null && SeasonRequireQuests.TryGetValue(season, out int quest) ? quest : 0;

    public record Grade(AllianceType Alliance, int Level, int RequirePoint);

    public record Log(int Id, AllianceType Alliance, int Grade);

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
