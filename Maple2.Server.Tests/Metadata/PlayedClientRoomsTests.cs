using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: which dungeon rooms the played client's own room table marks limitEnter, and the rooms'
// extra reward tickets with their coupon rows.
public class PlayedClientRoomsTests {
    private static XmlDocument Table(string rows) {
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{rows}</ms2>");
        return document;
    }

    private static bool Available(XmlElement row) => row.GetAttribute("feature") != "Off";

    [Test]
    public void LimitEnter_IsTheAvailableRowsThatSetIt() {
        XmlDocument table = Table("""
            <dungeonRoom dungeonRoomID="1" limitEnter="1" />
            <dungeonRoom dungeonRoomID="2" limitEnter="0" />
            <dungeonRoom dungeonRoomID="3" />
            <dungeonRoom dungeonRoomID="4" limitEnter="1" feature="Off" />
            <dungeonRoom dungeonRoomID="5" limitEnter="TRUE" />
            <dungeonRoom dungeonRoomID="x" limitEnter="1" />
            <other dungeonRoomID="6" limitEnter="1" />
            """);

        Assert.That(PlayedClientRooms.LimitEnter(table, Available).OrderBy(id => id), Is.EqualTo(new[] { 1, 5 }));
    }

    [Test]
    public void LimitEnter_AnEmptyTableLimitsNothing() {
        Assert.That(PlayedClientRooms.LimitEnter(Table(""), Available), Is.Empty);
    }

    // Shaped like the played client's coupon table (ids, tags and rates only).
    private static XmlDocument CouponTable() => Table("""
        <dungeonRewardCoupon id="1" ticketTag="DungeonRewardTicketA" baseItemID="20300478" maxExtraCount="8"><baseRate v="1" /></dungeonRewardCoupon>
        <dungeonRewardCoupon id="2" ticketTag="DungeonRewardTicketB" baseItemID="20300650"><baseRate v="1" /><baseRate v="2" /><baseRate v="4" /></dungeonRewardCoupon>
        <dungeonRewardCoupon id="3" ticketTag="DungeonRewardTicketC" canBuyInMeratMarket="false"><baseRate v="1" /></dungeonRewardCoupon>
        <dungeonRewardCoupon id="4" ticketTag="NoSuchTag"><baseRate v="1" /></dungeonRewardCoupon>
        <dungeonRewardCoupon id="5" ticketTag="DungeonRewardTicketA" feature="Off"><baseRate v="1" /></dungeonRewardCoupon>
        <dungeonRewardCoupon id="1" ticketTag="DungeonRewardTicketA" baseItemID="20300478" maxExtraCount="4" feature="Off"><baseRate v="9" /></dungeonRewardCoupon>
        """);

    [Test]
    public void Coupons_ReadTheAvailableRows() {
        Dictionary<int, DungeonExtraRewardTicket> coupons = PlayedClientRooms.Coupons(CouponTable(), Available);

        Assert.That(coupons.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 2, 3, 4 }));
        Assert.That(coupons[1].TicketTag, Is.EqualTo(ItemTag.DungeonRewardTicketA));
        Assert.That(coupons[1].BaseItemId, Is.EqualTo(20300478));
        Assert.That(coupons[1].MaxExtraCount, Is.EqualTo(8));
        Assert.That(coupons[1].BaseRates, Is.EqualTo(new[] { 1 }));
        Assert.That(coupons[1].CanBuyInMeratMarket, Is.True);
        Assert.That(coupons[2].MaxExtraCount, Is.EqualTo(0));
        Assert.That(coupons[2].BaseRates, Is.EqualTo(new[] { 1, 2, 4 }));
        Assert.That(coupons[3].CanBuyInMeratMarket, Is.False);
        Assert.That(coupons[4].TicketTag, Is.EqualTo(ItemTag.None));
    }

    // A later available row of an id replaces the earlier one (map operator[]).
    [Test]
    public void Coupons_ALaterRowOfAnIdWins() {
        XmlDocument table = Table("""
            <dungeonRewardCoupon id="1" ticketTag="DungeonRewardTicketA" maxExtraCount="8"><baseRate v="1" /></dungeonRewardCoupon>
            <dungeonRewardCoupon id="1" ticketTag="DungeonRewardTicketB" maxExtraCount="4"><baseRate v="2" /></dungeonRewardCoupon>
            """);

        DungeonExtraRewardTicket coupon = PlayedClientRooms.Coupons(table, Available)[1];
        Assert.That(coupon.TicketTag, Is.EqualTo(ItemTag.DungeonRewardTicketB));
        Assert.That(coupon.MaxExtraCount, Is.EqualTo(4));
    }

    [Test]
    public void ExtraRewardTickets_AreTheRoomsWithAKnownCoupon() {
        Dictionary<int, DungeonExtraRewardTicket> coupons = PlayedClientRooms.Coupons(CouponTable(), Available);
        XmlDocument rooms = Table("""
            <dungeonRoom dungeonRoomID="10" extraRewardTicketTableID="1" extraRewardTicketBaseCount="10" />
            <dungeonRoom dungeonRoomID="11" extraRewardTicketTableID="2" extraRewardTicketBaseCount="1" />
            <dungeonRoom dungeonRoomID="12" extraRewardTicketTableID="0" extraRewardTicketBaseCount="10" />
            <dungeonRoom dungeonRoomID="13" extraRewardTicketTableID="9" extraRewardTicketBaseCount="10" />
            <dungeonRoom dungeonRoomID="14" extraRewardTicketTableID="1" feature="Off" />
            <dungeonRoom dungeonRoomID="15" />
            <dungeonRoom dungeonRoomID="16" extraRewardTicketTableID="1" extraRewardTicketBaseCount="10" />
            <dungeonRoom dungeonRoomID="16" />
            """);

        Dictionary<int, DungeonExtraRewardTicket> tickets = PlayedClientRooms.ExtraRewardTickets(rooms, coupons, Available);

        Assert.That(tickets.Keys.OrderBy(id => id), Is.EqualTo(new[] { 10, 11 }));
        Assert.That(tickets[10].CouponId, Is.EqualTo(1));
        Assert.That(tickets[10].BaseCount, Is.EqualTo(10));
        Assert.That(tickets[11].BaseCount, Is.EqualTo(1));
        Assert.That(tickets[11].BaseRates, Is.EqualTo(new[] { 1, 2, 4 }));
    }

    [Test]
    public void HelperRewardDropBoxes_ReadEachAvailableRow() {
        XmlDocument rooms = Table("""
            <dungeonRoom dungeonRoomID="1" findHelperRewardDropBoxIds="330002188" />
            <dungeonRoom dungeonRoomID="2" findHelperRewardDropBoxIds="5, 6,,x" />
            <dungeonRoom dungeonRoomID="3" />
            <dungeonRoom dungeonRoomID="4" findHelperRewardDropBoxIds="7" feature="Off" />
            <dungeonRoom dungeonRoomID="3" findHelperRewardDropBoxIds="8" />
            """);

        Dictionary<int, int[]> boxes = PlayedClientRooms.HelperRewardDropBoxes(rooms, Available);

        Assert.That(boxes.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(boxes[1], Is.EqualTo(new[] { 330002188 }));
        Assert.That(boxes[2], Is.EqualTo(new[] { 5, 6 }));
        Assert.That(boxes[3], Is.EqualTo(new[] { 8 }));
    }
}
