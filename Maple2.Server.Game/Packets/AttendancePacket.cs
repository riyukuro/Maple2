using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class AttendancePacket {
    private enum Command : byte {
        Unknown5 = 5,
        StartedEvents = 6,
        Unknown7 = 7,
        Error = 9,
    }

    public static ByteWriter Unknown5() {
        var pWriter = Packet.Of(SendOp.Attendance);
        pWriter.Write<Command>(Command.Unknown5);

        return pWriter;
    }

    // The attendance events the player has started with an item: event id and the time the event ends for this
    // player (an item event's end is this time when it is set, GameEventAttendGiftCli::GetEndTime,
    // client/ms2/gameeventcli.cpp:564; the dialog shows it as the end of the period). The client
    // keeps them in a map (CMyCharacter::FUN_140d9f1c0, client/ms2/notifydebugstate.cpp:147) that the gift dialog
    // reads to show "item required" for a not yet started item event (uiattendgiftdialog.cpp:557) and the meret
    // attendance dialog for its locks (uicashattenddialog.cpp:477). KMS2 sends it at login.
    public static ByteWriter StartedEvents(ICollection<(int EventId, long EndTime)> started) {
        var pWriter = Packet.Of(SendOp.Attendance);
        pWriter.WriteByte((byte) Command.StartedEvents);
        pWriter.WriteInt(started.Count);
        foreach ((int eventId, long endTime) in started) {
            pWriter.WriteInt(eventId);
            pWriter.WriteLong(endTime);
        }

        return pWriter;
    }

    public static ByteWriter Unknown7() {
        var pWriter = Packet.Of(SendOp.Attendance);
        pWriter.Write<Command>(Command.Unknown7);
        pWriter.WriteInt();

        return pWriter;
    }

    public static ByteWriter Error(AttendanceError error) {
        var pWriter = Packet.Of(SendOp.Attendance);
        pWriter.Write<Command>(Command.Error);
        pWriter.Write<AttendanceError>(error);

        return pWriter;
    }
}
