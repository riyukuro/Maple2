namespace Maple2.Model.Metadata;

// table/individualarena.xml (CIndividualArenaDataTable::Load, individualarenadata.cpp): the arenas of the individual
// arena window, by arenaType.
public record IndividualArenaTable(IReadOnlyDictionary<int, IndividualArenaTable.Entry> Entries) : Table {
    public record Entry(
        int ArenaType,
        int FieldId,
        int PlayerCount,
        int RewardItemId1,
        int Reward1Winner,
        int Reward1Loser,
        int RewardItemId2,
        int Reward2Winner,
        int Reward2Loser);
}
