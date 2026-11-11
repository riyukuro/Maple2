using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

// The client's RoomTimerManager::Dispatch reads one command byte: 0 start, 1 update, 2 restart,
// 3 lap values, 4 stop, 5 cancel. Stop and cancel both close the timer window.
public static class RoomTimerPacket {
    private enum Command : byte {
        Start = 0,
        Modify = 1,
        Restart = 2,
        Laps = 3,
        Stop = 4,
        Cancel = 5,
    }

    public static ByteWriter Start(RoomTimer timer) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Start);
        pWriter.WriteByte((byte) timer.Type);
        pWriter.WriteInt(timer.StartTick);
        pWriter.WriteInt(timer.Duration);
        pWriter.WriteInt(); // offset in seconds, added to the client's tick

        return pWriter;
    }

    public static ByteWriter Modify(RoomTimer timer, int delta, bool showEffect = true) =>
        Modify(timer.StartTick, timer.Duration, delta, showEffect);

    // RoomTimerManager::Update: notice 1 shows the added or removed seconds and runs the timer's effect, 2 a fixed
    // notice, any other value (0 here) only moves the time.
    public static ByteWriter Modify(int startTick, int duration, int delta, bool showEffect) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Modify);
        pWriter.WriteInt(startTick);
        pWriter.WriteInt(duration); // the client closes the timer when this is 0 or less
        pWriter.WriteInt(showEffect ? 1 : 0);
        pWriter.WriteInt(delta);

        return pWriter;
    }

    // Unlike Start, the client opens the window for any type here.
    public static ByteWriter Restart(RoomTimer timer) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Restart);
        pWriter.WriteByte((byte) timer.Type);
        pWriter.WriteInt(timer.StartTick);
        pWriter.WriteInt(timer.Duration);
        pWriter.WriteInt(); // offset

        return pWriter;
    }

    // The gauge window places each lap icon at time / duration of its bar.
    public static ByteWriter Laps(IReadOnlyDictionary<int, int> laps) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Laps);
        pWriter.WriteInt(laps.Count);
        foreach ((int id, int time) in laps) {
            pWriter.WriteInt(id);
            pWriter.WriteInt(time);
        }

        return pWriter;
    }

    public static ByteWriter Stop() {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Stop);
        return pWriter;
    }

    public static ByteWriter Cancel() {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.WriteByte((byte) Command.Cancel);
        return pWriter;
    }
}
