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
            boxes[id] = row.GetAttribute("findHelperRewardDropBoxIds")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out int box) ? box : 0)
                .Where(box => box != 0)
                .ToArray();
        }
        return boxes;
    }

    private static int Number(XmlElement row, string name) => int.TryParse(row.GetAttribute(name), out int value) ? value : 0;

    private static bool IsOff(string value) => value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
}
