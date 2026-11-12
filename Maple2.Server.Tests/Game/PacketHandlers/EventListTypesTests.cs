using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The level-up package and the dungeon enter time limit in the event list, and the time limit at a room's entry.
public class EventListTypesTests {
    private const int EventId = 7001;
    private const int RoomId = 20000001;
    private const int LobbyId = 2000001;

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static string Written(GameEventType type, GameEventData data) {
        var test = new TestGameSession();
        GameEvent gameEvent = test.AddEvent(EventId, type, data);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        return Hex(writer);
    }

    private static string Name(string name) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(name);
        return Hex(writer);
    }

    [Test]
    public void LevelUpPackage_WritesItemAndQuestList() {
        // id 7001, item 20300001, then the quest list as text
        Assert.That(Written(GameEventType.LevelUpPackage, new LevelUpPackage(20300001, "11,12")),
            Is.EqualTo(Name("LevelUpPackage") + "591B0000" + "E1C03501" + "0500" + "310031002C0031003200"));
    }

    [Test]
    public void DungeonEnterTimeLimit_WritesEachRangeWithTheEventId() {
        var data = new DungeonEnterTimeLimit([new DungeonEnterTime(RoomId, 68400, 79200), new DungeonEnterTime(RoomId + 1, 0, 60)]);
        Assert.That(Written(GameEventType.DungeonEnterTimeLimit, data),
            Is.EqualTo(Name("DungeonEnterTimeLimit") + "591B0000" + "02000000"
                       + "591B0000" + "012D3101" + "300B0100" + "60350100"
                       + "591B0000" + "022D3101" + "00000000" + "3C000000"));
    }

    [Test]
    public void EnterTimeReader_ReadsRoomsAndRanges() {
        DungeonEnterTime[] times = DungeonEnterTimeReader.Parse(
            "<ms2><v dungeonID=\"20000001\" rangeTime=\"19:00-22:00\" /><v dungeonID=\"20000002\" rangeTime=\"06:30:15-07\" /></ms2>");
        Assert.That(times, Is.EqualTo(new[] {
            new DungeonEnterTime(20000001, 68400, 79200),
            new DungeonEnterTime(20000002, 23415, 25200),
        }));
    }

    [Test]
    public void EnterTimeReader_LeavesOutWhatTheClientLeavesOut() {
        // no room; a range that starts after its end. A range that is not two times is 0 to 0.
        DungeonEnterTime[] times = DungeonEnterTimeReader.Parse(
            "<ms2><v rangeTime=\"19:00-22:00\" /><v dungeonID=\"5\" rangeTime=\"22:00-19:00\" /><v dungeonID=\"6\" rangeTime=\"19:00\" /></ms2>");
        Assert.That(times, Is.EqualTo(new[] { new DungeonEnterTime(6, 0, 0) }));
        Assert.That(DungeonEnterTimeReader.Parse("not xml"), Is.Empty);
        Assert.That(DungeonEnterTimeReader.Parse(""), Is.Empty);
    }

    [TestCase(false, 0, true)] // a room without the limit is not tested
    [TestCase(true, 68399, false)]
    [TestCase(true, 68400, true)] // both ends are inside
    [TestCase(true, 79200, true)]
    [TestCase(true, 79201, false)]
    public void OpenAtTime_InsideARangeOfTheRoom(bool isLimitTime, int seconds, bool open) {
        DungeonEnterTime[] times = [new DungeonEnterTime(RoomId + 1, 0, 86399), new DungeonEnterTime(RoomId, 68400, 79200)];
        Assert.That(DungeonRoomRules.OpenAtTime(isLimitTime, RoomId, times, TimeSpan.FromSeconds(seconds)), Is.EqualTo(open));
    }

    [Test]
    public void OpenAtTime_NoRangeIsClosed() {
        Assert.That(DungeonRoomRules.OpenAtTime(true, RoomId, [], TimeSpan.FromHours(20)), Is.False);
    }

    private static List<string> Enter(bool isLimitTime, params DungeonEnterTime[] times) {
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(RoomId, LobbyId);
        room = room with { Limit = room.Limit with { IsLimitTime = isLimitTime } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [RoomId] = room }));
        if (times.Length > 0) {
            test.AddEvent(EventId, GameEventType.DungeonEnterTimeLimit, new DungeonEnterTimeLimit(times));
        }
        GameSession session = test.Start();
        test.TakeSent();
        session.Dungeon.CreateDungeonRoom(RoomId, false);
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static readonly string NotAllowTime = Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_NotAllowTime));

    [Test]
    public void CreateRoom_TimeLimitedRoomWithoutHours_IsRefused() {
        Assert.That(Enter(true), Is.EqualTo(new[] { NotAllowTime }));
        // the hours of another room do not open it
        Assert.That(Enter(true, new DungeonEnterTime(RoomId + 1, 0, 86399)), Is.EqualTo(new[] { NotAllowTime }));
    }

    [Test]
    public void CreateRoom_InsideItsHoursOrWithoutTheLimit_IsNotRefusedForTheTime() {
        Assert.That(Enter(true, new DungeonEnterTime(RoomId, 0, 86399)), Has.None.EqualTo(NotAllowTime));
        Assert.That(Enter(false), Has.None.EqualTo(NotAllowTime));
    }

    [Test]
    public void DungeonOpenPeriod_WritesTagStartAndEnd() {
        var test = new TestGameSession();
        var start = new DateTime(2018, 8, 30, 7, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameEvent gameEvent = test.AddEvent(EventId, GameEventType.DungeonOpenPeriod, new DungeonOpenPeriod("tag_a"), start, end);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        // id 7001, the tag, then start and end as seconds
        Assert.That(Hex(writer), Is.EqualTo(Name("DungeonOpenPeriod") + "591B0000" + "0500" + "7400610067005F006100"
                                              + "F095875B00000000" + "005786F400000000"));
    }

    [Test]
    public void OpenPeriodError_DateFirstThenTag() {
        var now = new DateTime(2020, 1, 10);
        Assert.That(DungeonRoomRules.OpenPeriodError(null, "", [], now), Is.Null);
        Assert.That(DungeonRoomRules.OpenPeriodError(null, "tag_a", ["tag_b"], now), Is.EqualTo(DungeonRoomError.s_room_dungeon_is_not_open_period));
        Assert.That(DungeonRoomRules.OpenPeriodError(null, "tag_a", ["tag_b", "tag_a"], now), Is.Null);
        Assert.That(DungeonRoomRules.OpenPeriodError(now.AddDays(1), "tag_a", ["tag_a"], now), Is.EqualTo(DungeonRoomError.s_room_dungeon_is_not_open_period_date));
        // from the date on the tag decides, as with no date
        Assert.That(DungeonRoomRules.OpenPeriodError(now.AddDays(-1), "tag_a", [], now), Is.EqualTo(DungeonRoomError.s_room_dungeon_is_not_open_period));
        Assert.That(DungeonRoomRules.OpenPeriodError(now.AddDays(-1), "tag_a", ["tag_a"], now), Is.Null);
        Assert.That(DungeonRoomRules.OpenPeriodError(now.AddDays(-1), "", [], now), Is.Null);
    }

    [Test]
    public void OpenPeriodTags_OnePeriodPerTag_TheSecondEventIsDropped() {
        var login = new DateTime(2020, 1, 10);
        DateTime Day(int day) => login.AddDays(day);
        DungeonRoomRules.OpenPeriod Period(int id, string tag, int start, int end) => new(id, tag, Day(start), Day(end));

        // one event: open from its start to its end
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", -1, 2)], login, Day(1)), Is.EquivalentTo(new[] { "tag_a" }));
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", 1, 2)], login, login), Is.Empty);
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", -1, 2)], login, Day(3)), Is.Empty);
        // two events of one tag running at login: the first of the list is kept, and the tag closes at its end
        // though the second still runs
        DungeonRoomRules.OpenPeriod[] both = [Period(1, "tag_a", -1, 2), Period(2, "tag_a", -2, 5)];
        Assert.That(DungeonRoomRules.OpenPeriodTags(both, login, Day(1)), Is.EquivalentTo(new[] { "tag_a" }));
        Assert.That(DungeonRoomRules.OpenPeriodTags(both, login, Day(3)), Is.Empty);
        // the second starts while the first is kept: dropped as well
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", -1, 2), Period(2, "tag_a", 1, 5)], login, Day(3)), Is.Empty);
        // the second starts after the first has ended: kept
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", -1, 2), Period(2, "tag_a", 3, 5)], login, Day(4)), Is.EquivalentTo(new[] { "tag_a" }));
        // a login after the first has ended sees only the second
        Assert.That(DungeonRoomRules.OpenPeriodTags(both, Day(3), Day(4)), Is.EquivalentTo(new[] { "tag_a" }));
        // other tags are apart; an event with no tag opens nothing
        Assert.That(DungeonRoomRules.OpenPeriodTags([Period(1, "tag_a", -1, 2), Period(2, "tag_b", -1, 2), Period(3, "", -1, 2)], login, Day(1)),
            Is.EquivalentTo(new[] { "tag_a", "tag_b" }));
    }

    // The check the dungeon match and party search share with the room request.
    [Test]
    public void ClosedError_DayHoursAndPeriod() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(RoomId, LobbyId);
        Assert.That(session.Dungeon.ClosedError(room), Is.Null);
        Assert.That(session.Dungeon.ClosedError(room with { Limit = room.Limit with { OpenPeriodTag = "tag_a" } }),
            Is.EqualTo(DungeonRoomError.s_room_dungeon_is_not_open_period));
        Assert.That(session.Dungeon.ClosedError(room with { Limit = room.Limit with { OpenPeriodDate = DateTime.Now.AddDays(1) } }),
            Is.EqualTo(DungeonRoomError.s_room_dungeon_is_not_open_period_date));
        DayOfWeek otherDay = DateTime.Now.AddDays(3).DayOfWeek;
        Assert.That(session.Dungeon.ClosedError(room with { Limit = room.Limit with { DayOfWeeks = [otherDay] } }),
            Is.EqualTo(DungeonRoomError.s_room_dungeon_canEnterDayOfWeeks));
    }

    [Test]
    public void ParseOpenPeriodDate_ReadsTheEventTimeForm() {
        Assert.That(DungeonRoomRules.ParseOpenPeriodDate("2019-12-06-07-30"), Is.EqualTo(new DateTime(2019, 12, 6, 7, 30, 0)));
        Assert.That(DungeonRoomRules.ParseOpenPeriodDate("2019-12-06-07-30-15"), Is.EqualTo(new DateTime(2019, 12, 6, 7, 30, 15)));
        Assert.That(DungeonRoomRules.ParseOpenPeriodDate(""), Is.Null);
        Assert.That(DungeonRoomRules.ParseOpenPeriodDate("soon"), Is.Null);
    }

    private static List<string> EnterTagged(string roomTag, DateTime? roomDate, string? eventTag, bool running = true) {
        var test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(RoomId, LobbyId);
        room = room with { Limit = room.Limit with { OpenPeriodTag = roomTag, OpenPeriodDate = roomDate } };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [RoomId] = room }));
        if (eventTag != null) {
            test.AddEvent(EventId, GameEventType.DungeonOpenPeriod, new DungeonOpenPeriod(eventTag),
                end: running ? null : DateTime.Now.AddDays(-1));
        }
        GameSession session = test.Start();
        test.TakeSent();
        session.Dungeon.CreateDungeonRoom(RoomId, false);
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static readonly string NotOpenPeriod = Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_is_not_open_period));
    private static readonly string NotOpenDate = Hex(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_is_not_open_period_date));

    [Test]
    public void CreateRoom_TaggedRoomWithoutItsRunningEvent_IsRefused() {
        Assert.That(EnterTagged("tag_a", null, null), Is.EqualTo(new[] { NotOpenPeriod }));
        Assert.That(EnterTagged("tag_a", null, "tag_b"), Is.EqualTo(new[] { NotOpenPeriod }));
        Assert.That(EnterTagged("tag_a", null, "tag_a", running: false), Is.EqualTo(new[] { NotOpenPeriod }));
        Assert.That(EnterTagged("", DateTime.Now.AddDays(1), null), Is.EqualTo(new[] { NotOpenDate }));
        Assert.That(EnterTagged("tag_a", DateTime.Now.AddDays(1), "tag_a"), Is.EqualTo(new[] { NotOpenDate }));
        // the date has passed and no event of the tag runs
        Assert.That(EnterTagged("tag_a", DateTime.Now.AddDays(-1), null), Is.EqualTo(new[] { NotOpenPeriod }));
    }

    [Test]
    public void CreateRoom_TaggedRoomInItsPeriodOrUntagged_IsNotRefusedForThePeriod() {
        Assert.That(EnterTagged("tag_a", null, "tag_a"), Has.None.EqualTo(NotOpenPeriod));
        Assert.That(EnterTagged("", null, null), Has.None.EqualTo(NotOpenPeriod).And.None.EqualTo(NotOpenDate));
        Assert.That(EnterTagged("", DateTime.Now.AddDays(-1), null), Has.None.EqualTo(NotOpenDate));
        Assert.That(EnterTagged("tag_a", DateTime.Now.AddDays(-1), "tag_a"), Has.None.EqualTo(NotOpenPeriod).And.None.EqualTo(NotOpenDate));
    }
}
