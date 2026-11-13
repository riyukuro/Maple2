using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// EnterUgcMap: the answer to entering a player-designed map (a home, an apartment room). The client reads a mode byte
// (CSceneStateGame::OnEnterUgcMap @0x1417373d0); modes 3 and 4 have a body, the others only show a common string
// (ids from commonstringtable.cpp).
public static class EnterUgcMapPacket {
    private enum Command : byte {
        NoRoom = 0, // s_enter_ugcmap_result_no_room (0x9cb)
        NotExistRoom = 1, // s_enter_ugcmap_result_not_exist_room (0x9cc), shown only in the apartment dialog's room box
        Expired = 2, // s_enter_ugcmap_result_expired (0x9cd)
        RequestPassword = 3,
        WrongPassword = 4,
        NotExistBuilding = 5, // s_enter_ugcmap_result_not_exist_building (0x9ce)
        Homeless = 6, // s_home_visit_failed_homeless (0x87c)
        VisitFailed = 7, // s_home_visit_failed_common (0x87b)
        DisabledMap = 8, // s_home_visit_failed_disablemap (0x87d)
        Dead = 9, // s_home_visit_failed_dead (0x87a)
        MyHome = 10, // s_home_visit_failed_myhome (0x87e)
        Private = 11, // s_home_visit_failed_private (by key)
    }

    // The password box (s_home_password_input). The client keeps the body and sends it back with the typed password:
    // with the last byte 1 as RequestMoveField 2 (visit home: the three ints and the long), else as RequestMoveField 0.
    public static ByteWriter RequestPassword(long accountId) {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.RequestPassword);
        pWriter.WriteInt(); // portal id
        pWriter.WriteInt();
        pWriter.WriteInt();
        pWriter.WriteLong(accountId);
        pWriter.WriteInt();
        pWriter.WriteByte(1);

        return pWriter;
    }

    // s_home_password_mismatch in the open box. The client counts the misses per map, ints and long; after 5 it refuses
    // a password itself for 10 seconds (UGCMapEnterBlockInfo, s_home_password_block_time).
    public static ByteWriter WrongPassword(long accountId) {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.WrongPassword);
        pWriter.WriteInt(); // portal id
        pWriter.WriteInt();
        pWriter.WriteInt();
        pWriter.WriteLong(accountId);

        return pWriter;
    }

    public static ByteWriter NoRoom() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.NoRoom);

        return pWriter;
    }

    public static ByteWriter NotExistRoom() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.NotExistRoom);

        return pWriter;
    }

    public static ByteWriter Expired() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.Expired);

        return pWriter;
    }

    public static ByteWriter NotExistBuilding() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.NotExistBuilding);

        return pWriter;
    }

    public static ByteWriter CannotVisitThatCharacterHome() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.Homeless);

        return pWriter;
    }

    public static ByteWriter VisitFailed() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.VisitFailed);

        return pWriter;
    }

    public static ByteWriter DisabledMap() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.DisabledMap);

        return pWriter;
    }

    public static ByteWriter Dead() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.Dead);

        return pWriter;
    }

    public static ByteWriter MyHome() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.MyHome);

        return pWriter;
    }

    public static ByteWriter Private() {
        var pWriter = Packet.Of(SendOp.EnterUgcMap);
        pWriter.WriteByte((byte) Command.Private);

        return pWriter;
    }
}
