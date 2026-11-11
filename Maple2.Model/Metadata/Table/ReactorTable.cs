namespace Maple2.Model.Metadata;

// table/reactor.xml joined with table/reactorstate.xml, rows already filtered by locale. A reactor is an event
// object a player places with a CreateReactor item (flower pots, chocolate); its states are listed in order and
// the packets carry the index into that list.
public record ReactorTable(IReadOnlyDictionary<int, ReactorTable.Entry> Entries) : Table {
    public record Entry(
        int Id,
        float ReactDistance,
        int ReactTimeMs,
        int GameEventId,
        State[] States,
        Reward[] Rewards);

    public record State(
        int Id,
        ReactorStateType Type,
        int NextTimeSec,
        int ExpireTimeSec,
        ConditionItem[] ConditionItems);

    // The client needs max(Consume, CheckCount) of the item to react; Rarity 0 means the item's own rarity.
    public record ConditionItem(int ItemId, int Rarity, int Consume, int CheckCount);

    public record Reward(int ItemId, int Rarity, int Amount);
}

// The client's state types: with Connect a player who is not yet connected may react, with Reward only a
// connected player may, with Sleep nobody may.
public enum ReactorStateType {
    None = 0,
    Connect = 1,
    Sleep = 2,
    Reward = 3,
}
