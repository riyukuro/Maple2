using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The mastery event (MasteryReward): value1 the mastery type (1 fishing, 2 music), value2 a rate per map. The rates
// of the running events of the type add up for the map and raise the mastery gained there by gain * rate / 10000.
// The ids are made up except where a test names a KR row.
public class MasteryRewardEventTests {
    private const int InstrumentItemId = 34000003;
    private const int ScoreItemId = 35000033;
    private const int InstrumentId = 1;
    private const int Map = 63000079;

    private const string KrFields = "<ms2><v fieldID=\"63000079\" rate=\"10000\"/><v fieldID=\"63000080\" rate=\"5000\"/></ms2>";

    private static GameEvent Event(int id, int type, params (int Map, int Rate)[] rates) =>
        new TestGameSession().AddEvent(id, GameEventType.MasteryReward,
            new MasteryRewardEvent(type, string.Empty, rates.ToDictionary(rate => rate.Map, rate => rate.Rate)));

    [Test]
    public void Reader_ReadsTheKrRow() {
        MasteryRewardEvent? reward = EventXmlReader.MasteryReward("2", KrFields);
        Assert.That(reward, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(reward!.Type, Is.EqualTo(2));
            Assert.That(reward.Fields, Is.EqualTo(KrFields)); // the client gets the text as stored
            Assert.That(reward.Rates, Is.EqualTo(new Dictionary<int, int> { [63000079] = 10000, [63000080] = 5000 }));
        });
    }

    [Test]
    public void Reader_SkipsWhatTheClientSkips() {
        Assert.Multiple(() => {
            // The first rate of a map counts; a zero or unreadable id or rate is skipped; the root's name is free.
            Assert.That(EventXmlReader.MasteryReward(" 1 ",
                    "<root><v fieldID=\" 063000079 \" rate=\"3000\"/><v fieldID=\"63000079\" rate=\"9000\"/>" +
                    "<v fieldID=\"0\" rate=\"100\"/><v fieldID=\"5\" rate=\"0\"/><v fieldID=\"x\" rate=\"100\"/>" +
                    "<v fieldID=\"6\" rate=\"-1\"/><v fieldID=\"7\" rate=\"4294967295\"/><w fieldID=\"8\" rate=\"100\"/></root>")!.Rates,
                Is.EqualTo(new Dictionary<int, int> { [63000079] = 3000 }));
            Assert.That(EventXmlReader.MasteryReward("0", KrFields), Is.Null); // the client's OnStart loads nothing
            Assert.That(EventXmlReader.MasteryReward("", KrFields), Is.Null);
            Assert.That(EventXmlReader.MasteryReward("-1", KrFields), Is.Null);
            Assert.That(EventXmlReader.MasteryReward("1", "not xml"), Is.Null);
            Assert.That(EventXmlReader.MasteryReward("1", null), Is.Null);
            Assert.That(EventXmlReader.MasteryReward("1", "<ms2><v fieldID=\"0\" rate=\"100\"/></ms2>"), Is.Null);
        });
    }

    [Test]
    public void Rate_AddsUpTheTypesEventsForTheMap() {
        GameEvent[] events = [
            Event(1, 1, (Map, 10000), (Map + 1, 500)),
            Event(2, 1, (Map, 2500)),
            Event(3, 2, (Map, 7000)), // music
        ];
        Assert.Multiple(() => {
            Assert.That(EventSaleRules.MasteryRewardRate(events, MasteryType.Fishing, Map), Is.EqualTo(12500));
            Assert.That(EventSaleRules.MasteryRewardRate(events, MasteryType.Fishing, Map + 1), Is.EqualTo(500));
            Assert.That(EventSaleRules.MasteryRewardRate(events, MasteryType.Music, Map), Is.EqualTo(7000));
            Assert.That(EventSaleRules.MasteryRewardRate(events, MasteryType.Fishing, Map + 2), Is.EqualTo(0));
            Assert.That(EventSaleRules.MasteryRewardRate(events, MasteryType.Mining, Map), Is.EqualTo(0));
            Assert.That(EventSaleRules.MasteryRewardRate([Event(1, 1, (Map, int.MaxValue)), Event(2, 1, (Map, 5))],
                MasteryType.Fishing, Map), Is.EqualTo(int.MaxValue));
        });
    }

    [TestCase(10, 10000, 20)]
    [TestCase(10, 2500, 12)] // 2.5 is cut
    [TestCase(10, 0, 10)]
    [TestCase(0, 10000, 0)]
    [TestCase(-3, 10000, -3)]
    [TestCase(int.MaxValue, 10000, int.MaxValue)]
    public void WithEvent_AddsTheShare(int gain, int rate, int expected) {
        GameEvent[] events = rate > 0 ? [Event(1, 1, (Map, rate))] : [];
        Assert.That(EventSaleRules.MasteryWithEvent(gain, events, MasteryType.Fishing, Map), Is.EqualTo(expected));
    }

    [Test]
    public void Event_WritesTheIdTheTypeAndTheFields() {
        GameEvent gameEvent = new TestGameSession().AddEvent(70001, GameEventType.MasteryReward,
            new MasteryRewardEvent(2, KrFields, new Dictionary<int, int> { [Map] = 10000 }));
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var expected = new ByteWriter();
        expected.WriteUnicodeString("MasteryReward");
        expected.WriteInt(70001);
        expected.WriteInt(2);
        expected.WriteUnicodeString(KrFields);
        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(Convert.ToHexString(expected.ToArray())));
        Assert.That(Convert.ToHexString(writer.ToArray()), Does.Contain("71110100" + "02000000"));
    }

    // A score played alone (PlayInstrument 3, then 4) in the event's map: a 60 s score giving 1 mastery a second, up to 1, gives 1,
    // the music event at 10000 doubles it; a fishing event or another map adds nothing.
    [TestCase(2, Map, 2)]
    [TestCase(1, Map, 1)]
    [TestCase(2, Map + 1, 1)]
    public void StopScore_MusicEventInTheMap(int type, int eventMap, int expected) {
        var test = new TestGameSession();
        test.Store(TestGameSession.Metadata(InstrumentItemId, type: 6,
            function: new ItemMetadataFunction(ItemFunction.OpenInstrument, "OpenInstrument", $"{InstrumentId}", false)));
        test.Store(TestGameSession.Metadata(ScoreItemId, type: 6) with {
            Music = new ItemMetadataMusic(10, 1, 1, false, 0, string.Empty, 60000),
        });
        test.SetTable(new InstrumentTable(new Dictionary<int, InstrumentMetadata> {
            [InstrumentId] = new(InstrumentId, 0, 1, 1, 1, 0),
        }));
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.musicMastery1] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetTable(new MasteryRewardTable(new Dictionary<MasteryType, IReadOnlyDictionary<int, MasteryRewardTable.Entry>>()));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.AddEvent(70001, GameEventType.MasteryReward,
            new MasteryRewardEvent(type, string.Empty, new Dictionary<int, int> { [eventMap] = 10000 }));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Map));
        Item instrument = session.Item.Inventory.Filter(item => item.Id == InstrumentItemId, InventoryType.FishingMusic).Single();
        Item score = session.Item.Inventory.Filter(item => item.Id == ScoreItemId, InventoryType.FishingMusic).Single();
        instrument.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        score.RemainUses = 5;
        test.TakeSent();
        var handler = new InstrumentHandler { TableMetadata = test.TableMetadata, BanWordStorage = null! };

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteLong(instrument.Uid);
            writer.WriteLong(score.Uid);
        }));
        Assume.That(session.Instrument, Is.Not.Null, "plays");
        session.Instrument!.StartTick -= 10_000;
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteByte(4)));

        Assert.That(session.Mastery[MasteryType.Music], Is.EqualTo(expected));
    }
}
