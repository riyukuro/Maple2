using System;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Validators;

// The DungeonExtraReward game event: its value1 rows (DungeonExtraRewardCountCtx::Load), the event as the client
// reads it (GameEventDungeonExtraReward::vfunc_4) and the event count of a room's record (DungeonRecordCli).
public class DungeonExtraRewardTests {
    private const string Rows = """
        <ms2>
            <v dungeonID="900301" count="2" cooldownType="dayOfWeeks" cooldownValue="6" />
            <v dungeonID="900302" count="1" cooldownType="NEXTDAY" />
            <v dungeonID="900303" count="0" cooldownType="nextDay" />
            <v dungeonID="0" count="3" />
            <v dungeonID="900304" count="4" cooldownType="1" cooldownValue="2" />
            <v dungeonID="900302" count="5" cooldownType="nextDay" />
        </ms2>
        """;

    private static long Epoch(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();

    [Test]
    public void RowsWithARoomAndACount() {
        DungeonExtraReward? reward = ServerTableMapper.ReadDungeonExtraReward(Rows);
        Assert.That(reward, Is.Not.Null);
        Assert.That(reward!.Entries, Is.EqualTo(new[] {
            new DungeonExtraReward.Entry(900301, 2, DungeonCooldownType.dayOfWeeks, 6),
            new DungeonExtraReward.Entry(900302, 5, DungeonCooldownType.nextDay, 0), // the later row of the room
            new DungeonExtraReward.Entry(900304, 4, DungeonCooldownType.none, 2), // a number is no type name
        }));
    }

    [TestCase("")]
    [TestCase("<ms2 />")]
    [TestCase("<ms2><v dungeonID=\"900301\" /></ms2>")]
    [TestCase("not xml")]
    public void NoRowNoEvent(string value1) {
        Assert.That(ServerTableMapper.ReadDungeonExtraReward(value1), Is.Null);
    }

    [Test]
    public void EventIsLaidOutForTheClient() {
        var data = new DungeonExtraReward([
            new DungeonExtraReward.Entry(900301, 2, DungeonCooldownType.dayOfWeeks, 6),
            new DungeonExtraReward.Entry(900302, 1, DungeonCooldownType.nextDay, 0),
        ]);
        var metadata = new GameEventMetadata(900090, GameEventType.DungeonExtraReward, DateTime.UnixEpoch, DateTime.UnixEpoch.AddDays(7),
            TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", "");
        var writer = new ByteWriter();
        new GameEvent(metadata).WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("DungeonExtraReward"));
        Assert.That(reader.ReadInt(), Is.EqualTo(900090));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        foreach ((int dungeonId, int count) in new[] { (900301, 2), (900302, 1) }) {
            Assert.That(reader.ReadInt(), Is.EqualTo(dungeonId)); // the map key
            Assert.That(reader.ReadInt(), Is.EqualTo(dungeonId));
            Assert.That(reader.ReadInt(), Is.EqualTo(900090));
            Assert.That(reader.ReadInt(), Is.EqualTo(count));
        }
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    [Test]
    public void RecordWritesTheEventCountExpiry() {
        var record = new DungeonRecord(900301) { ExtraClears = 2, ExtraCooldownTimestamp = 5000, ClearTimestamp = 1000 };
        var writer = new ByteWriter();
        writer.WriteClass(record);
        var reader = new ByteReader(writer.ToArray(), 0);
        reader.Skip(4 + 8 + 1 + 1 + 8 + 1);
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.ReadLong(), Is.EqualTo(5000)); // m_extraCount's expire, not the first clear
    }

    [Test]
    public void FirstEventByIdNamesTheRoom() {
        var early = new DungeonExtraReward([new DungeonExtraReward.Entry(900301, 2, DungeonCooldownType.nextDay, 0)]);
        var late = new DungeonExtraReward([
            new DungeonExtraReward.Entry(900301, 9, DungeonCooldownType.nextDay, 0),
            new DungeonExtraReward.Entry(900302, 3, DungeonCooldownType.nextDay, 0),
        ]);
        var events = new[] { (900092, late), (900091, early) };

        Assert.That(DungeonRoomRules.ExtraRewardEntry(events, 900301)?.Entry.Count, Is.EqualTo(2));
        Assert.That(DungeonRoomRules.ExtraRewardEntry(events, 900302)?.EventId, Is.EqualTo(900092));
        Assert.That(DungeonRoomRules.ExtraRewardEntry(events, 900303), Is.Null);
    }

    [Test]
    public void EventCountGivesACountPastTheRewardCount() {
        long now = Epoch(new DateTime(2025, 1, 1, 15, 0, 0));
        var record = new DungeonRecord(900301) {
            UnionClears = 3, UnionCooldownTimestamp = now + 1000, UnionSubClears = 1, UnionSubCooldownTimestamp = now + 1000,
        };
        Assert.That(DungeonRoomRules.HasCountLeft(record, 3, 0, now), Is.False);
        Assert.That(DungeonRoomRules.HasCountLeft(record, 3, 0, now, eventCount: 2), Is.True);
        // before the daily limit, as the client tests the event count first
        Assert.That(DungeonRoomRules.HasCountLeft(record, 3, 1, now, eventCount: 2), Is.True);

        record.ExtraClears = 2;
        record.ExtraCooldownTimestamp = now + 1000;
        Assert.That(DungeonRoomRules.HasCountLeft(record, 3, 0, now, eventCount: 2), Is.False);
        Assert.That(DungeonRoomRules.ExtraCountNow(record, now + 1000), Is.EqualTo(0)); // expired
        Assert.That(DungeonRoomRules.HasCountLeft(record, 3, 0, now + 999, eventCount: 2), Is.False);
    }

    // 2025-01-01 is a Wednesday.
    [Test]
    public void UseExtraCountUntilTheCooldownEnds() {
        var entry = new DungeonExtraReward.Entry(900301, 2, DungeonCooldownType.dayOfWeeks, 6); // Friday
        var eventEnd = new DateTime(2025, 2, 1);
        var record = new DungeonRecord(900301);
        var clear = new DateTime(2025, 1, 1, 15, 0, 0);

        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, clear, eventEnd), Is.True);
        Assert.That(record.ExtraClears, Is.EqualTo(1));
        Assert.That(record.ExtraCooldownTimestamp, Is.EqualTo(Epoch(new DateTime(2025, 1, 3))));
        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, clear.AddHours(1), eventEnd), Is.True);
        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, clear.AddHours(2), eventEnd), Is.False);
        Assert.That(record.ExtraClears, Is.EqualTo(2));

        // the next period starts again
        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, new DateTime(2025, 1, 3, 9, 0, 0), eventEnd), Is.True);
        Assert.That(record.ExtraClears, Is.EqualTo(1));
        Assert.That(record.ExtraCooldownTimestamp, Is.EqualTo(Epoch(new DateTime(2025, 1, 10))));
    }

    [Test]
    public void NoCooldownTypeLastsTheEvent() {
        var entry = new DungeonExtraReward.Entry(900301, 1, DungeonCooldownType.none, 0);
        var eventEnd = new DateTime(2025, 2, 1);
        var record = new DungeonRecord(900301);
        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, new DateTime(2025, 1, 1, 15, 0, 0), eventEnd), Is.True);
        Assert.That(record.ExtraCooldownTimestamp, Is.EqualTo(Epoch(eventEnd)));
        Assert.That(DungeonRoomRules.UseExtraCount(record, entry, new DateTime(2025, 1, 20), eventEnd), Is.False);
    }
}
