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

    // A member's contribution of one kind (CGuildXmlData::GetContribution, guilddef_2.cpp:661): the kind's row, 0
    // without one (as data ingested before the table was read). The type is matched without case, as the client's
    // CompareNoCase (commonenum.cpp:688); the stored dictionary keeps no comparer.
    public int ContributionOf(string kind) {
        if (Contributions == null) {
            return 0;
        }
        if (Contributions.TryGetValue(kind, out int value)) {
            return value;
        }
        foreach ((string type, int amount) in Contributions) {
            if (string.Equals(type, kind, StringComparison.OrdinalIgnoreCase)) {
                return amount;
            }
        }
        return 0;
    }
}

// The contribution kinds of table/guildcontribution.xml (Parse_14021a0a0, commonenum.cpp:688).
public static class GuildContributionKind {
    public const string Attend = "attend";
    public const string Donation = "donation";
    public const string WinMiniGame = "winMiniGame";
    public const string LoseMiniGame = "loseMiniGame";
    public const string DailyQuest = "dailyQuest";
    public const string WeeklyQuest = "weeklyQuest";
    public const string Raid = "raid";
    public const string WinPvp = "winPvp";
    public const string LosePvp = "losePvp";
}
