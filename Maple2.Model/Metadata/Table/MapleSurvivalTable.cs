namespace Maple2.Model.Metadata;

// The NA Maple Survival tables (the restored client's lobby and pass windows read the same files):
// table/na/maplesurvivalopenperiod.xml (the seasons and the pass price), survivallevel.xml (the exp a level needs),
// survivallevelreward.xml (the items a level gives) and survivalpassreward(_paid).xml (the pass rewards by the
// period's survivalRewardID and level).
public record MapleSurvivalTable(
    IReadOnlyDictionary<int, MapleSurvivalTable.Period> Periods,
    IReadOnlyDictionary<int, long> LevelExp,
    IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> LevelRewards,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> FreePass,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> PaidPass) : Table {

    public record Period(int Id, int RewardId, DateTime StartTime, DateTime EndTime, int PassPrice);

    // type: item, genderItem (Ids, Values and Counts hold the male then the female entry) or additionalEffect
    // (Values: the level); Values are an item's rarity.
    public record Reward(string Type, int[] Ids, int[] Values, int[] Counts);
}
