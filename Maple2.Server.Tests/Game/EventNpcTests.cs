using System;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The SpawnNpc game event: an npc the event puts at a region spawn of a field, with a shop of its own.
public class EventNpcTests {
    private static GameEventMetadata Row(int id, GameEventData data, DateTime start, DateTime end) =>
        new(id, GameEventType.SpawnNpc, start, end, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    // The rows as the KR data writes them (13000055 and 13000009): the field id keeps its leading zero.
    [Test]
    public void Reader_RowWithShop() {
        SpawnNpcEvent? spawn = EventXmlReader.SpawnNpc("""<npc fieldID="02000064" spawnPointID="10" npcID="11001003" shopID="254" />""");
        Assert.That(spawn, Is.EqualTo(new SpawnNpcEvent(2000064, 10, 11001003, 254)));
    }

    [Test]
    public void Reader_RowWithoutShop() {
        SpawnNpcEvent? spawn = EventXmlReader.SpawnNpc("""<npc fieldID="02000064" spawnPointID="5" npcID="11001002" />""");
        Assert.That(spawn, Is.EqualTo(new SpawnNpcEvent(2000064, 5, 11001002, 0)));
    }

    // The client reads the root whatever its name (tableexporter.cpp:906).
    [Test]
    public void Reader_AnyRootName() {
        Assert.That(EventXmlReader.SpawnNpc("""<ms2 fieldID="2000188" spawnPointID="1" npcID="11000624" shopID="316" />"""),
            Is.EqualTo(new SpawnNpcEvent(2000188, 1, 11000624, 316)));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("<npc")]
    [TestCase("""<npc spawnPointID="5" npcID="11001002" />""")]
    [TestCase("""<npc fieldID="2000064" npcID="11001002" />""")]
    [TestCase("""<npc fieldID="2000064" spawnPointID="5" />""")]
    [TestCase("""<npc fieldID="2000064" spawnPointID="5" npcID="x" />""")]
    public void Reader_Unusable(string? value1) {
        Assert.That(EventXmlReader.SpawnNpc(value1), Is.Null);
    }

    [Test]
    public void Reader_NegativeShopIsNone() {
        Assert.That(EventXmlReader.SpawnNpc("""<npc fieldID="2000064" spawnPointID="5" npcID="11001002" shopID="-1" />""")!.ShopId, Is.Zero);
    }

    // The client has no class for it: the event list leaves it out.
    [Test]
    public void NotSentToClient() {
        var gameEvent = new GameEvent(Row(1, new SpawnNpcEvent(2000064, 5, 11001002, 0), DateTime.MinValue, DateTime.MaxValue));
        Assert.That(gameEvent.SentToClient, Is.False);
    }

    // The field's rows running now, lowest id first; other fields, ended or later rows and other types are left out.
    [Test]
    public void ForField_RunningRowsOfTheField() {
        var now = new DateTime(2026, 11, 12, 12, 0, 0);
        var bobby = new SpawnNpcEvent(2000064, 10, 11001003, 254);
        var ruby = new SpawnNpcEvent(2000064, 5, 11001002, 0);
        GameEventMetadata[] events = [
            Row(13000055, bobby, new DateTime(2023, 6, 8), new DateTime(2099, 7, 15)),
            Row(13000009, ruby, new DateTime(2020, 11, 16), new DateTime(2099, 12, 31)),
            Row(13000015, new SpawnNpcEvent(2020041, 5, 11001002, 0), new DateTime(2020, 11, 16), new DateTime(2099, 12, 31)),
            Row(13000070, new SpawnNpcEvent(2000064, 7, 11000001, 0), new DateTime(2020, 1, 1), new DateTime(2021, 1, 1)),
            Row(13000071, new SpawnNpcEvent(2000064, 8, 11000002, 0), new DateTime(2030, 1, 1), new DateTime(2099, 1, 1)),
            Row(1, new MiniGameBonusReward(["oxquiz"], []), DateTime.MinValue, DateTime.MaxValue),
        ];
        Assert.Multiple(() => {
            Assert.That(EventNpcRules.ForField(events, 2000064, now), Is.EqualTo(new[] { ruby, bobby }));
            Assert.That(EventNpcRules.ForField(events, 2020041, now), Has.Exactly(1).Items);
            Assert.That(EventNpcRules.ForField(events, 2000188, now), Is.Empty);
        });
    }

    // The event's shop replaces the npc's own (Bobby's 282 becomes 254); without one the npc keeps its own.
    [TestCase(254, 282, 254)]
    [TestCase(0, 282, 282)]
    [TestCase(0, 0, 0)]
    [TestCase(316, 0, 316)]
    public void ShopId_EventShopFirst(int eventShopId, int npcShopId, int expected) {
        Assert.That(EventNpcRules.ShopId(eventShopId, npcShopId), Is.EqualTo(expected));
    }
}
