using System.Xml;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// The dungeon rooms whose entry the played client limits, read from that client's own room table
// (table/dungeonroom.xml). DungeonRoom::Load (commonfiles/dungeonroom.cpp:377) reads the row's limitEnter
// bool and GetLimitType gives 2 for it; the room window then shows the cooldown icon while the record has no
// count left (UIDungeonRoomInfo::IsCooldown, uidungeonroomdialog.cpp:1798). The table keeps the available
// rows only (FUN_14028faa0 with IsElementAvailable, commonutil.cpp:1748).
public static class PlayedClientRooms {
    public const string RoomTable = "table/dungeonroom.xml";
    public const string CouponTable = "table/dungeonrewardcoupon.xml";

    public static HashSet<int> LimitEnter(XmlDocument roomTable, Func<XmlElement, bool> available) {
        var ids = new HashSet<int>();
        XmlNodeList? rows = roomTable.SelectNodes("ms2/dungeonRoom");
        if (rows == null) {
            return ids;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row) || !int.TryParse(row.GetAttribute("dungeonRoomID"), out int id)) {
                continue;
            }
            string value = row.GetAttribute("limitEnter");
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                ids.Add(id);
            }
        }
        return ids;
    }

    // The coupon rows by id, as DungeonRoomTable::LoadCoupons (commonfiles/dungeonroom.cpp:1553) keeps them: the
    // available <dungeonRewardCoupon> rows, a later row of an id replacing an earlier one (map operator[]). The
    // ticketTag name is the item tag of that name (None when the server has no such tag); canBuyInMeratMarket is
    // true unless the row sets it off (the DungeonRewardCoupon constructor's default).
    public static Dictionary<int, DungeonExtraRewardTicket> Coupons(XmlDocument couponTable, Func<XmlElement, bool> available) {
        var coupons = new Dictionary<int, DungeonExtraRewardTicket>();
        XmlNodeList? rows = couponTable.SelectNodes("ms2/dungeonRewardCoupon");
        if (rows == null) {
            return coupons;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row)) {
                continue;
            }
            int id = Number(row, "id");
            var rates = new List<int>();
            foreach (XmlNode rate in row.SelectNodes("baseRate")!) {
                if (rate is XmlElement element) {
                    rates.Add(Number(element, "v"));
                }
            }
            coupons[id] = new DungeonExtraRewardTicket(
                CouponId: id,
                BaseCount: 0,
                TicketTag: System.Enum.TryParse(row.GetAttribute("ticketTag"), out ItemTag tag) && System.Enum.IsDefined(tag) ? tag : ItemTag.None,
                BaseItemId: Number(row, "baseItemID"),
                MaxExtraCount: Number(row, "maxExtraCount"),
                BaseRates: rates.ToArray(),
                CanBuyInMeratMarket: !IsOff(row.GetAttribute("canBuyInMeratMarket")));
        }
        return coupons;
    }

    // Each available room row with an extraRewardTicketTableID whose coupon exists: that coupon with the row's
    // extraRewardTicketBaseCount (DungeonRoom::Load, dungeonroom.cpp:396; DungeonRoomTable::LinkRoom links the
    // coupon, :1518). A later row of a room replaces an earlier one.
    public static Dictionary<int, DungeonExtraRewardTicket> ExtraRewardTickets(XmlDocument roomTable, IReadOnlyDictionary<int, DungeonExtraRewardTicket> coupons,
            Func<XmlElement, bool> available) {
        var tickets = new Dictionary<int, DungeonExtraRewardTicket>();
        XmlNodeList? rows = roomTable.SelectNodes("ms2/dungeonRoom");
        if (rows == null) {
            return tickets;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row) || !int.TryParse(row.GetAttribute("dungeonRoomID"), out int id)) {
                continue;
            }
            int couponId = Number(row, "extraRewardTicketTableID");
            if (couponId == 0 || !coupons.TryGetValue(couponId, out DungeonExtraRewardTicket? coupon)) {
                tickets.Remove(id);
                continue;
            }
            tickets[id] = coupon with { BaseCount = Number(row, "extraRewardTicketBaseCount") };
        }
        return tickets;
    }

    // Each available room row's findHelperRewardDropBoxIds (DungeonRoom::Load, dungeonroom.cpp:433, a comma list of
    // box ids), empty when the row sets none. A later row of a room replaces an earlier one.
    public static Dictionary<int, int[]> HelperRewardDropBoxes(XmlDocument roomTable, Func<XmlElement, bool> available) {
        var boxes = new Dictionary<int, int[]>();
        XmlNodeList? rows = roomTable.SelectNodes("ms2/dungeonRoom");
        if (rows == null) {
            return boxes;
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row) || !int.TryParse(row.GetAttribute("dungeonRoomID"), out int id)) {
                continue;
            }
            boxes[id] = BoxIds(row.GetAttribute("findHelperRewardDropBoxIds"));
        }
        return boxes;
    }

    // The room's reward boxes as the client lists them: rewardLimitedDropBoxIds, then rewardLimitedDropBoxIds2
    // (dungeonroom.cpp:391-395).
    public static int[] LimitedDropBoxIds(int[] ids, int[]? ids2) => ids.Concat(ids2 ?? []).ToArray();

    // A comma list of box ids (XmlTokenList), box 0 left out.
    private static int[] BoxIds(string value) {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => int.TryParse(text, out int box) ? box : 0)
            .Where(box => box != 0)
            .ToArray();
    }

    // The room's rules the played client reads from its own row (DungeonRoom::Load, commonfiles/dungeonroom.cpp:382-431)
    // and tests or shows itself: the reward count its room window counts down (DungeonRecordCli::HasCountLeft), the
    // reward reset day (GetCooldownType), the user counts and party-only flag of its enter buttons, the gear score
    // and level it lists, the revival buttons (UIRevivalDialog) and the revival count. An attribute the row leaves
    // out is 0 (the DungeonRoom constructor's defaults, :351-358). The server's room table is another locale's (NA),
    // and ClientListedRows puts the client row's attributes over the NA row, so an attribute only the NA row sets
    // stayed: KR rooms whose client shows the meret revive had it closed (59, NA limitMeratRevival), the meso revive
    // closed (7: 23040003, 23100001, the guild raids 26001001-26005001), a gear limit (6: 20008001 1500) and a
    // minimum party (29006001: 4). The row's durationSeconds is the room's time (DurationTick, in ms): the parser of
    // the NA table knows only its durationTick, so the 33 KR rows with a time had none and their rooms no clock (the
    // NA rows that share a room set the same 900 s, but 23006003 and 23010003 are 1200 s in KR). Its
    // rewardLimitedDropBoxIds2 the client appends to the room's rewardLimitedDropBoxIds as the reward list its room
    // window shows (m_rewardDropBoxIds, dungeonroom.cpp:391-395); the parser dropped it, so the 41 KR rooms with one
    // (boxes 20001002 or 20001001 in 26 of them) gave the clear without it. Of two available rows of a room the one select keeps is read, as the client's
    // table load keeps the one CFeatureManager::SelectFeatureElement picks (FUN_14028faa0, commonutil.cpp:1748; the
    // ingest passes PlayedNpcStats.SelectFeatureElement). KMS2 has no such duplicate today.
    // The row's reward values the client reads too (dungeonroom.cpp:383-394; PlayedRoomReward): taken from the NA row,
    // 5 KR chaos raids (23006003, 23010003, 23021003, 23022003, 23023003) kept an NA unionRewardID whose union row
    // is NA-only, so their clears gave no reward at all (DungeonManager finds no union room and stops); 6 kept an
    // NA seasonRankRewardID, the Colosseum 24096001 NA's exp and meso (685000, 14000; KR none) and the event room
    // 29006001 NA's account-wide reward count.
    public static Dictionary<int, PlayedRoomRules> Rules(XmlDocument roomTable, Func<XmlElement, bool> available,
            Func<XmlElement, XmlElement, XmlElement> select) {
        var selected = new Dictionary<int, XmlElement>();
        XmlNodeList? rows = roomTable.SelectNodes("ms2/dungeonRoom");
        if (rows == null) {
            return [];
        }
        foreach (XmlNode node in rows) {
            if (node is not XmlElement row || !available(row) || !int.TryParse(row.GetAttribute("dungeonRoomID"), out int id)) {
                continue;
            }
            selected[id] = selected.TryGetValue(id, out XmlElement? kept) ? select(kept, row) : row;
        }
        var rules = new Dictionary<int, PlayedRoomRules>();
        foreach ((int id, XmlElement row) in selected) {
            rules[id] = new PlayedRoomRules(
                RewardCount: Number(row, "rewardCount"),
                CooldownValue: Number(row, "cooldownValue"),
                MinUserCount: Number(row, "minUserCount"),
                MaxUserCount: Number(row, "maxUserCount"),
                GearScore: Number(row, "gearScore"),
                MinLevel: Number(row, "limitPlayerLevel"),
                PartyOnly: IsOn(row.GetAttribute("isPartyOnly")),
                DisableMesoRevival: IsOn(row.GetAttribute("limitMesoRevival")),
                DisableMeretRevival: IsOn(row.GetAttribute("limitMeratRevival")),
                MaxRevivalCount: Number(row, "defaultRevivalLimitCount"),
                DurationTick: (int) Math.Min(Math.Max(Number(row, "durationSeconds"), 0) * 1000L, int.MaxValue),
                LimitedDropBoxIds2: BoxIds(row.GetAttribute("rewardLimitedDropBoxIds2")),
                UseRandomMatch: IsOn(row.GetAttribute("isUseRandomMatch")),
                Reward: new PlayedRoomReward(
                    SubRewardCount: Number(row, "subRewardCount"),
                    AccountWide: IsOn(row.GetAttribute("isAccountReward")),
                    UnionRewardId: Number(row, "unionRewardID"),
                    Exp: long.TryParse(row.GetAttribute("rewardExp"), out long exp) ? exp : 0,
                    ExpRate: float.TryParse(row.GetAttribute("rewardExpRate"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float rate) ? rate : 0f,
                    Meso: long.TryParse(row.GetAttribute("rewardMeso"), out long meso) ? meso : 0,
                    ScoreBonusId: Number(row, "scoreBonusId"),
                    SeasonRankRewardId: Number(row, "seasonRankRewardID")));
        }
        return rules;
    }

    private static int Number(XmlElement row, string name) => int.TryParse(row.GetAttribute(name), out int value) ? value : 0;

    private static bool IsOn(string value) => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static bool IsOff(string value) => value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The rules of a dungeon room as the played client's own row gives them (PlayedClientRooms.Rules).</summary>
public record PlayedRoomRules(
    int RewardCount,
    int CooldownValue,
    int MinUserCount,
    int MaxUserCount,
    int GearScore,
    int MinLevel,
    bool PartyOnly,
    bool DisableMesoRevival,
    bool DisableMeretRevival,
    int MaxRevivalCount,
    int DurationTick = 0,
    int[]? LimitedDropBoxIds2 = null,
    PlayedRoomReward? Reward = null,
    // isUseRandomMatch (dungeonroom.cpp:434): the room dialog's Party 33 party-search button.
    bool UseRandomMatch = false);

/// <summary>
/// The reward values of a dungeon room as the played client's own row gives them (DungeonRoom::Load,
/// dungeonroom.cpp:383-394; an attribute the row leaves out is 0). Null in rules made from the server's row.
/// </summary>
public record PlayedRoomReward(
    int SubRewardCount,
    bool AccountWide,
    int UnionRewardId,
    long Exp,
    float ExpRate,
    long Meso,
    int ScoreBonusId,
    int SeasonRankRewardId);
