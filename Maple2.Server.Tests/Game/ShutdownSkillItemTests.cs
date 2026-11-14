using System;
using System.Text.Json;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The ShutdownSkillItem game event: while it runs the client refuses to cast the skill of an item it lists
// (MyPC::FUN_141525580, message 0x2f7). The server reads the row as the client does, sends it in the event list and
// refuses the same casts.
public class ShutdownSkillItemTests {
    private static GameEventMetadata Row(int id, GameEventData data) =>
        new(id, GameEventType.ShutdownSkillItem, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    // The root's <v> children, in id order, 0 and repeats dropped; other elements and ids no item has are skipped.
    [Test]
    public void Reader_Ids() {
        ShutdownSkillItem? row = EventXmlReader.ShutdownSkillItem(
            "<ms2><v itemId=\"50200002\"/><v itemId=\"0\"/><x itemId=\"7\"/><v itemId=\"50200001\"/><v itemId=\"50200002\"/>"
            + "<v itemId=\"4294967295\"/><v itemId=\"-3\"/><v/></ms2>");
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.ItemIds, Is.EqualTo(new[] { 50200001, 50200002 }));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("<ms2")]
    [TestCase("<ms2 />")]
    [TestCase("<ms2><v itemId=\"0\"/></ms2>")]
    public void Reader_Unusable(string? value1) {
        Assert.That(EventXmlReader.ShutdownSkillItem(value1), Is.Null);
    }

    // GameEventShutdownSkillItem::vfunc_4: the id, then the count and each item id.
    [Test]
    public void Packet() {
        var gameEvent = new GameEvent(Row(900001, new ShutdownSkillItem([50200001, 50200002])));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.Multiple(() => {
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("ShutdownSkillItem"));
            Assert.That(reader.ReadInt(), Is.EqualTo(900001));
            Assert.That(reader.ReadInt(), Is.EqualTo(2));
            Assert.That(reader.ReadInt(), Is.EqualTo(50200001));
            Assert.That(reader.ReadInt(), Is.EqualTo(50200002));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
        });
    }

    [Test]
    public void JsonRoundTrip() {
        GameEventData data = new ShutdownSkillItem([3, 9]);
        GameEventData? back = JsonSerializer.Deserialize<GameEventData>(JsonSerializer.Serialize(data));
        Assert.That(back, Is.TypeOf<ShutdownSkillItem>());
        Assert.That(((ShutdownSkillItem) back!).ItemIds, Is.EqualTo(new[] { 3, 9 }));
    }

    [Test]
    public void Rule_AnyEventListingTheItem() {
        GameEvent[] events = [new(Row(1, new ShutdownSkillItem([5, 8]))), new(Row(2, new ShutdownSkillItem([12])))];
        Assert.Multiple(() => {
            Assert.That(EventSaleRules.SkillItemShutdown(events, 8), Is.True);
            Assert.That(EventSaleRules.SkillItemShutdown(events, 12), Is.True);
            Assert.That(EventSaleRules.SkillItemShutdown(events, 6), Is.False);
            Assert.That(EventSaleRules.SkillItemShutdown([], 8), Is.False);
        });
    }

    // The cast check: the first motion of a cast whose inventory item a running event lists is refused; a later
    // motion and a cast with no inventory item are not, and the events are not asked for them.
    [TestCase(false, 8, ExpectedResult = true)]
    [TestCase(false, 6, ExpectedResult = false)]
    [TestCase(true, 8, ExpectedResult = false)]
    [TestCase(false, null, ExpectedResult = false)]
    public bool CastRule(bool laterMotion, int? itemId) {
        bool asked = false;
        bool refused = SkillCastRules.ItemShutdownRefuses(laterMotion, itemId, () => {
            asked = true;
            return [new GameEvent(Row(1, new ShutdownSkillItem([5, 8])))];
        });
        Assert.That(asked, Is.EqualTo(!laterMotion && itemId != null));
        return refused;
    }
}
