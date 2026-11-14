using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

// Room 23050003 as the KR row has it: a 900 s clock (durationSeconds, isExpireTimeOut) over two fields, 2000551 and
// 2000552, entered from the lobby 50000016. The room's time counts once for the run: the second field joins the
// running clock, and the lobby, a waiting map outside fieldIDs, runs none.
public class DungeonRoomClockTests {
    private const int Room = 23050003;
    private const int Lobby = 50000016;
    private const int Stage1 = 2000551;
    private const int Stage2 = 2000552;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private (DungeonFieldManager Lobby, DungeonFieldManager Stage1, DungeonFieldManager Stage2) Enter() {
        DungeonRoomMetadata room = TestGameSession.DungeonRoom(Room, Lobby) with {
            DurationTick = 900_000,
            FieldIds = [Stage1, Stage2],
            TimerType = DungeonTimerType.clock,
            IsExpireTimeOut = true,
        };
        test.SetTable(new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> { [Room] = room }));
        test.Start();
        test.EnterDungeon(room);
        var lobby = (DungeonFieldManager) test.Session.Field!;
        return (lobby, test.AddRoomField(Stage1), test.AddRoomField(Stage2));
    }

    [Test]
    public void TheLobby_RunsNoTimer() {
        (DungeonFieldManager lobby, _, _) = Enter();

        lobby.StartRoomTimer();

        Assert.That(lobby.RoomTimer, Is.Null);
    }

    [Test]
    public void TheSecondField_GetsTheTimeLeft() {
        (_, DungeonFieldManager stage1, DungeonFieldManager stage2) = Enter();

        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);
        stage2.StartRoomTimer();
        stage2.RoomTimer!.Update(600_000);

        Assert.Multiple(() => {
            Assert.That(stage2.RoomTimer.Clock, Is.SameAs(stage1.RoomTimer.Clock));
            Assert.That((stage2.RoomTimer.StartTick, stage2.RoomTimer.Duration), Is.EqualTo((1_000, 900_000)));
            Assert.That(stage2.RoomTimer.Counting, Is.True);
            Assert.That(stage2.RoomTimer.TimedOut(901_000), Is.False);
            Assert.That(stage2.RoomTimer.TimedOut(901_001), Is.True);
        });
    }

    // Time a field's npc adds or a script takes counts for the whole room.
    [Test]
    public void AddedTime_CountsInEveryField() {
        (_, DungeonFieldManager stage1, DungeonFieldManager stage2) = Enter();
        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);
        stage2.StartRoomTimer();

        Assert.That(stage1.RoomTimer.Extend(60_000), Is.True);

        Assert.That(stage2.RoomTimer!.Duration, Is.EqualTo(960_000));
    }

    // A stop or restart one field's script makes reaches the other fields of the room: their window closes or opens
    // again, and a restarted time counts down to the room's close there too.
    [Test]
    public void StopAndRestart_ReachEveryField() {
        (_, DungeonFieldManager stage1, DungeonFieldManager stage2) = Enter();
        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);
        stage2.StartRoomTimer();
        stage2.RoomTimer!.Update(2_000);

        stage1.RoomTimer.Stop();
        stage2.RoomTimer.Update(3_000);
        Assert.That((stage2.RoomTimer.Closed, stage2.RoomTimer.Counting), Is.EqualTo((true, false)));

        stage1.RoomTimer.Restart(60_000);
        stage2.RoomTimer.Update(4_000);
        Assert.That((stage2.RoomTimer.Closed, stage2.RoomTimer.Counting), Is.EqualTo((false, true)));
    }

    // A field entered after the room's time was stopped opens no window.
    [Test]
    public void AFieldEnteredAfterTheStop_StaysClosed() {
        (_, DungeonFieldManager stage1, DungeonFieldManager stage2) = Enter();
        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);
        stage1.RoomTimer.Stop();

        stage2.StartRoomTimer();

        Assert.That(stage2.RoomTimer!.Closed, Is.True);
    }

    // The lobby runs no timer, but its waiting players are sent back when the room's time runs out (isExpireTimeOut).
    [Test]
    public void TheLobby_ClosesWithTheRoom() {
        (DungeonFieldManager lobby, DungeonFieldManager stage1, _) = Enter();
        Assert.That(lobby.LobbyExpires(10_000_000), Is.False, "no field started the time");

        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);

        Assert.Multiple(() => {
            Assert.That(lobby.LobbyExpires(901_000), Is.False);
            Assert.That(lobby.LobbyExpires(901_001), Is.True);
            Assert.That(stage1.LobbyExpires(901_001), Is.False, "only the lobby");
        });
    }

    [Test]
    public void TheLobby_StaysWhenTheTimeWasStopped() {
        (DungeonFieldManager lobby, DungeonFieldManager stage1, _) = Enter();
        stage1.StartRoomTimer();
        stage1.RoomTimer!.Update(1_000);
        stage1.RoomTimer.Stop();

        Assert.That(lobby.LobbyExpires(10_000_000), Is.False);
    }
}
