using System.Linq;
using System.Xml;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Metadata;

// MS2_PLAYED_CLIENT_XML: which dungeon rooms the played client's own room table marks limitEnter.
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
}
