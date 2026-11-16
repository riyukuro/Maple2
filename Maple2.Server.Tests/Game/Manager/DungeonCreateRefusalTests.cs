using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Game.Manager;

// The refusals of a dungeon room request (DungeonManager.CreateDungeonRoom), each answered with one packet and no
// room: the room list, the enter limit, the day of the week, a count-limited room and the size check.
public class DungeonCreateRefusalTests {
    private const int Room = 23050101;
    private TestGameSession test = null!;

    private void Start(Func<DungeonRoomMetadata, DungeonRoomMetadata>? edit = null) {
        test = new TestGameSession();
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, 50000016);
        if (edit != null) {
            room = edit(room);
        }
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.TakeSent();
    }

    private SendOp[] Ops() => test.TakeSent().Select(TestGameSession.Op).ToArray();

    [Test]
    public void AnUnknownRoom_TheRoomError() {
        Start();
        test.Session.Dungeon.CreateDungeonRoom(99999999, withParty: false);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }

    [Test]
    public void ABlockingEnterLimit_TheEntranceError() {
        Start(room => room with { Limit = room.Limit with { MinLevel = 99 } });
        test.Session.Dungeon.CreateDungeonRoom(Room, withParty: false);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.FieldEntrance }));
    }

    [Test]
    public void ARoomClosedToday_TheDayError() {
        DayOfWeek other = (DayOfWeek) (((int) DateTime.Now.DayOfWeek + 1) % 7);
        Start(room => room with { Limit = room.Limit with { DayOfWeeks = [other] } });
        test.Session.Dungeon.CreateDungeonRoom(Room, withParty: false);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }

    // A limitEnter room without a reward count left refuses, with no ticket asked for.
    [Test]
    public void ALimitEnterRoomWithNoCountLeft_TheCooldownError() {
        Start(room => room with { LimitEnter = true, Reward = room.Reward with { Count = 1 } });
        test.Session.Dungeon.Records[Room].UnionClears = 1;
        test.Session.Dungeon.Records[Room].UnionCooldownTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        test.Session.Dungeon.CreateDungeonRoom(Room, withParty: false);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }

    [Test]
    public void SoloIntoARoomOfAMinimumUserCount_TheUnderMinError() {
        Start(room => room with { Limit = room.Limit with { MinUserCount = 2 } });
        test.Session.Dungeon.CreateDungeonRoom(Room, withParty: false);
        Assert.That(Ops(), Is.EqualTo(new[] { SendOp.RoomDungeon }));
    }
}
