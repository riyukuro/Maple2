using System;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The NpcEventDrop game event: while it runs, a killed npc it matches gives its dealers the items of its drop boxes.
public class NpcEventDropTests {
    private static GameEventMetadata Row(int id, GameEventData data, DateTime start, DateTime end) =>
        new(id, GameEventType.NpcEventDrop, start, end, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");

    private static NpcEventDrop Drop(int[] npcIds, int[] dropIds, int fieldId = 0, short minLevel = 0, short maxLevel = 0, int rank = 0, int tag = 0) =>
        new(minLevel, maxLevel, fieldId, rank, npcIds, true, dropIds, tag);

    // The rows as the KR data writes them (280001 and 280016).
    [Test]
    public void Reader_NpcRow() {
        NpcEventDrop? drop = EventXmlReader.NpcEventDrop("""<ms2><v customNpcId="21090187,21090188,21090189" isShow="true" dropId="700000001" /></ms2>""");
        Assert.That(drop, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(drop!.NpcIds, Is.EqualTo(new[] { 21090187, 21090188, 21090189 }));
            Assert.That(drop.DropIds, Is.EqualTo(new[] { 700000001 }));
            Assert.That(drop.IsShow, Is.True);
            Assert.That(drop.FieldId, Is.Zero);
            Assert.That((drop.MinLevel, drop.MaxLevel, drop.NpcRank, drop.EventTag), Is.EqualTo(((short) 0, (short) 0, 0, 0)));
        });
    }

    [Test]
    public void Reader_FieldRow() {
        NpcEventDrop? drop = EventXmlReader.NpcEventDrop("""<ms2><v fieldId="2000014" maxLevel="99" isShow="true" dropId="700000022" /></ms2>""");
        Assert.Multiple(() => {
            Assert.That(drop!.FieldId, Is.EqualTo(2000014));
            Assert.That(drop.MaxLevel, Is.EqualTo(99));
            Assert.That(drop.NpcIds, Is.Empty);
            Assert.That(drop.DropIds, Is.EqualTo(new[] { 700000022 }));
        });
    }

    // The client reads the root's first element whatever the names; npcRank is a number, eventTags a tag name.
    [Test]
    public void Reader_AnyNamesRankAndTag() {
        NpcEventDrop? drop = EventXmlReader.NpcEventDrop("""<root><x minLevel="10" npcRank="3" eventTags="Dungeon_Boss" dropId="25010005" /><v dropId="1" /></root>""");
        Assert.Multiple(() => {
            Assert.That(drop!.MinLevel, Is.EqualTo(10));
            Assert.That(drop.NpcRank, Is.EqualTo(3));
            Assert.That(drop.EventTag, Is.EqualTo(3));
            Assert.That(drop.IsShow, Is.False);
            Assert.That(drop.DropIds, Is.EqualTo(new[] { 25010005 }));
        });
    }

    // customNpcId is a set in the client, dropId a vector: a repeated npc counts once, a repeated box stays.
    [Test]
    public void Reader_RepeatsKeptInDropIdOnly() {
        NpcEventDrop? drop = EventXmlReader.NpcEventDrop("""<ms2><v customNpcId="5,5,6" dropId="7,7,8" /></ms2>""");
        Assert.Multiple(() => {
            Assert.That(drop!.NpcIds, Is.EqualTo(new[] { 5, 6 }));
            Assert.That(drop.DropIds, Is.EqualTo(new[] { 7, 7, 8 }));
        });
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("<ms2")]
    [TestCase("<ms2 />")]
    [TestCase("""<ms2><v customNpcId="21090187" /></ms2>""")]
    [TestCase("""<ms2><v customNpcId="21090187" dropId="x,0" /></ms2>""")]
    public void Reader_Unusable(string? value1) {
        Assert.That(EventXmlReader.NpcEventDrop(value1), Is.Null);
    }

    // The client has a class for it with no reader of its own: the list carries the name and the id only.
    [Test]
    public void SentWithItsIdOnly() {
        var gameEvent = new GameEvent(Row(280001, Drop([21090187], [700000001]), DateTime.MinValue, DateTime.MaxValue));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("NpcEventDrop"));
        Assert.That(reader.ReadInt(), Is.EqualTo(280001));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void Matches_EachSetCondition() {
        Assert.Multiple(() => {
            Assert.That(NpcEventDropRules.Matches(Drop([], [1]), 21000001, 50, 1, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([21000001], [1]), 21000001, 50, 1, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([21000002], [1]), 21000001, 50, 1, 2000014), Is.False);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], fieldId: 2000014), 21000001, 50, 1, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], fieldId: 2000017), 21000001, 50, 1, 2000014), Is.False);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], maxLevel: 99), 21000001, 99, 1, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], maxLevel: 49), 21000001, 50, 1, 2000014), Is.False);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], minLevel: 50), 21000001, 50, 1, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], minLevel: 51), 21000001, 50, 1, 2000014), Is.False);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], rank: 3), 21000001, 50, 3, 2000014), Is.True);
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], rank: 3), 21000001, 50, 1, 2000014), Is.False);
            // The server's npcs carry no event tags.
            Assert.That(NpcEventDropRules.Matches(Drop([], [1], tag: 1), 21000001, 50, 1, 2000014), Is.False);
        });
    }

    // The running rows the npc matches, lowest id first; ended, later and other-npc rows give nothing.
    [Test]
    public void DropIds_RunningMatchingRows() {
        DateTime now = new(2026, 11, 14, 12, 0, 0);
        GameEventMetadata[] rows = [
            Row(280003, Drop([21000001], [700000003, 700000004]), now.AddDays(-1), now.AddDays(1)),
            Row(280001, Drop([21000001], [700000001]), now.AddDays(-1), now.AddDays(1)),
            Row(280002, Drop([21000002], [700000002]), now.AddDays(-1), now.AddDays(1)),
            Row(280004, Drop([21000001], [700000005]), now.AddDays(-3), now.AddDays(-2)),
            Row(280005, Drop([21000001], [700000006]), now.AddDays(1), now.AddDays(2)),
        ];
        Assert.That(NpcEventDropRules.DropIds(rows, 21000001, 50, 1, 2000014, now), Is.EqualTo(new[] { 700000001, 700000003, 700000004 }));
    }
}
