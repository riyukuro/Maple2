using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class RevivalPacket {
    public static ByteWriter UpdatePenalty(int objectId, int endTick, int count) {
        var pWriter = Packet.Of(SendOp.RevivalConfirm);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(endTick);
        pWriter.WriteInt(count);

        return pWriter;
    }

    public static ByteWriter RevivalCount(int count) {
        var pWriter = Packet.Of(SendOp.RevivalCount);
        pWriter.WriteInt(count);

        return pWriter;
    }

    // Client: CSceneStateGame::OnDungeonReviveCount (scenestategame.cpp:5074): int revivals used in this dungeon.
    // UIRevivalDialog::GetFirstButtonLabel (uirevivaldialog.cpp:787) shows the room's defaultRevivalLimitCount minus
    // this value on the first button, and turns the button into "leave the dungeon" when nothing is left.
    public static ByteWriter DungeonReviveCount(int used) {
        var pWriter = Packet.Of(SendOp.DungeonReviveCount);
        pWriter.WriteInt(used);

        return pWriter;
    }

    public static ByteWriter Revive(int objectId) {
        var pWriter = Packet.Of(SendOp.Revival);
        pWriter.WriteInt(objectId);
        pWriter.WriteByte();

        return pWriter;
    }

    public static ByteWriter Tombstone(Tombstone tombstone) {
        var pWriter = Packet.Of(SendOp.Tombstone);
        pWriter.WriteClass<Tombstone>(tombstone);

        return pWriter;
    }
}
