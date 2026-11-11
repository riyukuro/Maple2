using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record GuildTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> Buffs,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, GuildTable.House>> Houses,
    IReadOnlyDictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>> Npcs,
    IReadOnlyDictionary<short, GuildTable.Property> Properties,
    // table/guildevent.xml by id. Null when the data was ingested before the table was read.
    IReadOnlyDictionary<int, GuildTable.Event>? Events = null) : Table {

    public record Event(
        int Id,
        int MapId);

    public record Buff(
        int Id,
        short Level,
        short RequireLevel,
        int Cost,
        int UpgradeCost,
        int Duration,
        int Cooldown = 0);

    public record House(
        int MapId,
        int RequireLevel,
        int UpgradeCost,
        int ReThemeCost,
        int[] Facilities);

    public record Npc(
        GuildNpcType Type,
        short Level,
        int RequireGuildLevel,
        int RequireHouseLevel,
        int UpgradeCost);

    public record Property(
        short Level,
        int Experience,
        byte Capacity,
        long FundMax,
        int DonateMax,
        int CheckInExp,
        int WinMiniGameExp,
        int LoseMiniGameExp,
        int RaidExp,
        int CheckInFund,
        int WinMiniGameFund,
        int LoseMiniGameFund,
        int RaidFund,
        float CheckInPlayerExpRate,
        float DonatePlayerExpRate,
        int CheckInCoin,
        int DonateCoin,
        int WinMiniGameCoin,
        int LoseMiniGameCoin);
}
