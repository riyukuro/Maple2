using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Core.Packets;

// Whenever server sends 02, client makes a request
//
// 01 and 03 seem to be the first ones sent after entering game
// perhaps setting some initial state?
public static class TimeSyncPacket {
    private enum Command : byte {
        Response = 0,
        Reset = 1,
        Request = 2,
        Set = 3,
    }

    public static ByteWriter Response(DateTimeOffset time, int key) {
        var pWriter = Packet.Of(SendOp.ResponseTimeSync);
        pWriter.Write<Command>(Command.Response);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteLong(time.ToUnixTimeSeconds()); // CMainSystem[28], CMainSystem[30]
        pWriter.WriteInt(ZoneSeconds(time, TimeZoneInfo.Local));
        pWriter.WriteByte(); // Daylight hours: the zone seconds already hold daylight saving.
        pWriter.WriteInt(key);

        return pWriter;
    }

    public static ByteWriter Reset(DateTimeOffset time) {
        var pWriter = Packet.Of(SendOp.ResponseTimeSync);
        pWriter.Write<Command>(Command.Reset);
        pWriter.WriteInt(Environment.TickCount);
        pWriter.WriteLong(time.ToUnixTimeSeconds()); // CMainSystem[28], CMainSystem[30]
        pWriter.WriteInt(ZoneSeconds(time, TimeZoneInfo.Local));
        pWriter.WriteByte(); // Daylight hours: the zone seconds already hold daylight saving.

        return pWriter;
    }

    // The zone the client's day runs in, CMsTime::SetTimeZone(zone, dst) (mstime.cpp:273): its local time is UTC minus
    // zone plus dst hours (CalcZoneOffset), so the zone is the seconds west of UTC (KST -32400). The server's day
    // (daily reset, alliance missions, quest resets) runs in its local time, so the client gets that zone; with 0 the
    // client counts the reset to UTC midnight.
    public static int ZoneSeconds(DateTimeOffset time, TimeZoneInfo zone) {
        return -(int) zone.GetUtcOffset(time).TotalSeconds;
    }

    // Request client to make a request
    public static ByteWriter Request() {
        var pWriter = Packet.Of(SendOp.ResponseTimeSync);
        pWriter.Write<Command>(Command.Request);

        return pWriter;
    }

    public static ByteWriter Set(DateTimeOffset time) {
        var pWriter = Packet.Of(SendOp.ResponseTimeSync);
        pWriter.Write<Command>(Command.Set);
        pWriter.WriteLong(time.ToUnixTimeSeconds()); // CMainSystem[32]

        return pWriter;
    }
}
