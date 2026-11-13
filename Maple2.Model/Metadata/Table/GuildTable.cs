using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record GuildTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> Buffs,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, GuildTable.House>> Houses,
    IReadOnlyDictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>> Npcs,
    IReadOnlyDictionary<short, GuildTable.Property> Properties,
    // table/guildevent.xml by id. Null when the data was ingested before the table was read.
    IReadOnlyDictionary<int, GuildTable.Event>? Events = null,
    // table/guildquestrewardfactor.xml by the guild's quest npc level. Null when the data was ingested before the
    // table was read.
    IReadOnlyDictionary<short, GuildTable.QuestRewardFactor>? QuestRewardFactors = null,
    // table/guildraid.xml by dungeon room id. Null when the data was ingested before the table was read.
    IReadOnlyDictionary<int, GuildTable.Raid>? Raids = null,
    // table/guildcontribution.xml: a member's contribution by its type (winMiniGame, loseMiniGame, ...). Null when the
    // data was ingested before the table was read.
    IReadOnlyDictionary<string, int>? Contributions = null) : Table {

    // A guild raid (CGuildXmlData::GuildRaid, guilddef_2.cpp:433): its dungeon room, the raid points of a clear and
    // the guild fund and exp factors of a clear (1 when the row has none, as the client's defaults).
    public record Raid(
        int DungeonRoomId,
        int ClearRaidPoint,
        float ClearGuildFund,
        float ClearGuildExp);

    // The factors a guild quest's complete reward is scaled by at one quest npc level.
    public record QuestRewardFactor(
        float GuildExp,
        float UserExp,
        float GuildFund,
        float UserMeso,
        float GuildCoin);

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
