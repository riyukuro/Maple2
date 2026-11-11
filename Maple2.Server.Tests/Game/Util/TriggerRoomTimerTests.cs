using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Util;

public class TriggerRoomTimerTests {
    [Test]
    public void DungeonRoom_TimerFromTheRoomRow() {
        // A dungeon room with timerType clock or gauge and a duration gets that timer.
        Assert.That(DungeonFieldManager.RoomTimerTypeOf(DungeonTimerType.clock, 900000), Is.EqualTo(RoomTimerType.Clock));
        Assert.That(DungeonFieldManager.RoomTimerTypeOf(DungeonTimerType.gauge, 900000), Is.EqualTo(RoomTimerType.Gauge));
        Assert.That(DungeonFieldManager.RoomTimerTypeOf(DungeonTimerType.none, 900000), Is.Null);
        Assert.That(DungeonFieldManager.RoomTimerTypeOf(DungeonTimerType.clock, 0), Is.Null);
    }

    [Test]
    public void DungeonTimeout_PassesWhenTheTimeRanOut() {
        var clock = new RoomTimerClock(900000);
        clock.Start(1000);
        Assert.That(clock.TimedOut(901000), Is.False);
        Assert.That(clock.TimedOut(901001), Is.True);
    }

    [Test]
    public void DungeonSetEndTime_EndsTheTimeoutButNotTheRoomExpiry() {
        var clock = new RoomTimerClock(1000);
        clock.Start(0);
        clock.End();
        Assert.That(clock.TimedOut(5000), Is.False);
        // A room with isExpireTimeOut still closes at its time.
        Assert.That(clock.Expired(5000), Is.True);
    }

    [Test]
    public void DungeonStopTimer_StopsTheTime() {
        var clock = new RoomTimerClock(1000);
        clock.Start(0);
        clock.Stop(400);
        clock.Stop(800); // a second stop keeps the first time
        Assert.That(clock.StoppedTick, Is.EqualTo(400));
        Assert.That(clock.Expired(5000), Is.False);
        Assert.That(clock.TimedOut(5000), Is.False);
    }

    [Test]
    public void DungeonResetTime_RunsTheTimerAgainFromNow() {
        var clock = new RoomTimerClock(900000);
        clock.Start(0);
        clock.End();
        clock.Stop(10);
        clock.Restart(2000, 300 * 1000);
        Assert.That(clock.StartTick, Is.EqualTo(2000));
        Assert.That(clock.Duration, Is.EqualTo(300000));
        Assert.That(clock.TimedOut(302000), Is.False);
        Assert.That(clock.TimedOut(302001), Is.True);
    }

    [Test]
    public void DungeonLapTime_MarksAreTimesFromTheStart() {
        var clock = new RoomTimerClock(900000);
        clock.Start(1000);
        clock.SetLap(1, 420000);
        clock.SetLap(2, 720000);
        clock.MoveLapToNow(1, 301000);
        Assert.That(clock.Laps, Is.EqualTo(new SortedDictionary<int, int> { [1] = 300000, [2] = 720000 }));
    }

    [Test]
    public void RoomTimerPacket_LapsAsTheClientReadsThem() {
        // RoomTimerManager::ReadValues: int count, then per mark an unsigned id and an int time.
        ByteWriter writer = RoomTimerPacket.Laps(new SortedDictionary<int, int> { [1] = 420000, [2] = 720000 });
        var reader = new ByteReader(writer.Buffer, 0);
        reader.ReadShort(); // opcode
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(420000));
        Assert.That(reader.ReadInt(), Is.EqualTo(2));
        Assert.That(reader.ReadInt(), Is.EqualTo(720000));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));

        // Stop (4) and cancel (5) are the command byte alone.
        Assert.That(RoomTimerPacket.Stop().Length, Is.EqualTo(3));
        Assert.That(RoomTimerPacket.Stop().Buffer[2], Is.EqualTo(4));
        Assert.That(RoomTimerPacket.Cancel().Buffer[2], Is.EqualTo(5));
    }
}
