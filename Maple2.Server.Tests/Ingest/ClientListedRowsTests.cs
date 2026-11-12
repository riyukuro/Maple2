using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using M2dXmlGenerator;
using Maple2.File.Ingest.Helpers;
using Maple2.File.Parser.Enum;
using Maple2.File.Parser.Xml.Table;

namespace Maple2.Server.Tests.Ingest;

// LOCALE=KR over the NA pack: a room the filter leaves without a row is kept when the played client lists it, with the
// client's values over the pack's. Synthetic rows.
[NonParallelizable]
public class ClientListedRowsTests {
    private Dictionary<string, int> oldFeatures = null!;
    private string oldLocale = null!;
    private static readonly XmlSerializer RoomSerializer = new(typeof(DungeonRoomRoot));

    [SetUp]
    public void SetFilter() {
        oldFeatures = FeatureLocaleFilter.Features;
        oldLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = "KR";
        FeatureLocaleFilter.Features = new Dictionary<string, int> { ["Season1"] = 1, ["Season2"] = 2 };
    }

    [TearDown]
    public void RestoreFilter() {
        FeatureLocaleFilter.Features = oldFeatures;
        FeatureLocaleFilter.Locale = oldLocale;
    }

    private const string Pack = """
        <ms2>
          <dungeonRoom dungeonRoomID="1" feature="Season1" dungeonLevel="10" groupType="normal" playerCountFactorID="1000" />
          <dungeonRoom dungeonRoomID="2" feature="PackOnlyFeature" dungeonLevel="28" playType="limitReward" groupType="normal" rewardCount="3" playerCountFactorID="4001" dungeonHelperRequireClearCount="10" rewardUnlimitedDropBoxIds="10,33" locale="NA" />
          <dungeonRoom dungeonRoomID="3" feature="Season1" dungeonLevel="30" groupType="normal" locale="NA" />
          <dungeonRoom dungeonRoomID="4" feature="Season1" dungeonLevel="40" groupType="normal" locale="NA" />
          <dungeonRoom dungeonRoomID="5" feature="Season1" dungeonLevel="50" playType="limitReward" groupType="normal" locale="NA" />
          <dungeonRoom dungeonRoomID="6" feature="Season1" dungeonLevel="60" groupType="normal" locale="CN" />
        </ms2>
        """;

    // The client's copy: 1 has its own pack row; 2 differs from the NA row; 3 is not listed; 4's row has a feature that
    // is off; 5 sets a playType the parser cannot read; an empty
    // value (2's rewardCount, 5's playerCountFactorID) is no value; 6 has only a CN row in the pack.
    private const string Client = """
        <ms2>
          <dungeonRoom dungeonRoomID="1" feature="Season1" dungeonLevel="11" />
          <dungeonRoom dungeonRoomID="2" feature="Season2" dungeonLevel="28" groupType="1" playerCountFactorID="1000" dungeonHelperRequireClearCount="1" rewardLimitedDropBoxIds="2" rewardCount="" />
          <dungeonRoom dungeonRoomID="4" feature="Season9" dungeonLevel="40" />
          <dungeonRoom dungeonRoomID="5" feature="Season1" dungeonLevel="55" playType="7" playerCountFactorID="" />
          <dungeonRoom dungeonRoomID="6" feature="Season1" dungeonLevel="60" />
        </ms2>
        """;

    private static XmlDocument Doc(string xml) {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static (ClientListedRows.Counts Counts, Dictionary<int, DungeonRoom> Rooms) Run() {
        XmlDocument server = Doc(Pack);
        ClientListedRows.Counts counts = ClientListedRows.Add(server, Doc(Client), "dungeonRoom", "dungeonRoomID", "KR", "NA",
            ClientListedRows.Available, ClientListedRows.RowReads(RoomSerializer));
        using var reader = new XmlNodeReader(server);
        var root = (DungeonRoomRoot) RoomSerializer.Deserialize(reader)!;
        return (counts, root.dungeonRoom.ToDictionary(room => room.dungeonRoomID));
    }

    [Test]
    public void Add_KeepsTheListedRoomsOnly() {
        (ClientListedRows.Counts counts, Dictionary<int, DungeonRoom> rooms) = Run();
        Assert.Multiple(() => {
            Assert.That(rooms.Keys.Order(), Is.EqualTo(new[] { 1, 2, 5 }));
            Assert.That((counts.Ids, counts.Kept, counts.Added, counts.Dropped), Is.EqualTo((6, 1, 2, 3)));
            Assert.That(counts.AddedIds, Is.EqualTo(new[] { "2", "5" }));
        });
    }

    // A room with its own row for the locale (or none) is the pack's: the client row does not change it.
    [Test]
    public void Add_LeavesAKeptRoomAsItIs() {
        Assert.That(Run().Rooms[1].dungeonLevel, Is.EqualTo(10));
    }

    // Where the client row sets a value, it wins (its feature too); a value only the pack's row has stays.
    [Test]
    public void Add_TakesTheClientValues() {
        DungeonRoom room = Run().Rooms[2];
        Assert.Multiple(() => {
            Assert.That(room.playerCountFactorID, Is.EqualTo(1000));
            Assert.That(room.dungeonHelperRequireClearCount, Is.EqualTo(1));
            Assert.That(room.rewardLimitedDropBoxIds, Is.EqualTo(new[] { 2 }));
            Assert.That(room.groupType, Is.EqualTo(DungeonGroupType.normal));
            Assert.That(room.Feature, Is.EqualTo("Season2"));
            Assert.That(room.Locale, Is.EqualTo("KR"));
            Assert.That(room.playType, Is.EqualTo(DungeonPlayType.limitReward));
            Assert.That(room.rewardCount, Is.EqualTo(3));
            Assert.That(room.rewardUnlimitedDropBoxIds, Is.EqualTo(new[] { 10, 33 }));
        });
    }

    // A client value the parser cannot read keeps the pack's value, and is named; an empty client value is skipped.
    [Test]
    public void Add_KeepsThePackValueTheParserCannotRead() {
        (ClientListedRows.Counts counts, Dictionary<int, DungeonRoom> rooms) = Run();
        Assert.Multiple(() => {
            Assert.That(rooms[5].dungeonLevel, Is.EqualTo(55));
            Assert.That(rooms[5].playType, Is.EqualTo(DungeonPlayType.limitReward));
            Assert.That(counts.PackValues, Is.EqualTo(new[] { "5.playType" }));
        });
    }
}
