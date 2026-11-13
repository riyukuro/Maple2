using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// HideAndSeek 0x12B (client CSceneStateGame::OnHideAndSeek): a byte mode, then the int object id of the player it is
// about. Mode 3 is an int object id; for the client's own character it ends the hide-and-seek slot mode, shows the
// hud again (the hide reason the arcade's PlayArcade 0 set) and closes the arcade life bar dialog. The arcades close
// their life bar with it. The Hide and Seek game (HideAndSeekField) sends 0, 5, 6 and 8.
public static class HideAndSeekPacket {
    private enum Command : byte {
        Info = 0,
        End = 3,
        AddSkillSet = 5,
        RemoveSkillSet = 6,
        Result = 8,
    }

    // 0: the player's game info (HideAndSeek::Info::FUN_14050a320): in the game, caught, a third flag, the exp, the
    // kill count, the team (0 none, 1 hiders, 2 seekers), and for a hider one more int. The result dialog lists the
    // players with a team, with their kills, catch ("death") and exp; the in-game flag and the team make the
    // client's team id (0xc9 or 0xca).
    public static ByteWriter Info(int objectId, bool inGame, bool caught, long exp, int kills, byte team, int hiderValue = 0) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.Info);
        pWriter.WriteInt(objectId);
        pWriter.WriteBool(inGame);
        pWriter.WriteBool(caught);
        pWriter.WriteBool(false);
        pWriter.WriteLong(exp);
        pWriter.WriteInt(kills);
        pWriter.WriteByte(team);
        if (team == 1) {
            pWriter.WriteInt(hiderValue);
        }
        return pWriter;
    }

    // 5 and 6: the client's own character adds or removes a skill set and applies its skills.
    public static ByteWriter AddSkillSet(int objectId, int skillSetId) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.AddSkillSet);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(skillSetId);
        return pWriter;
    }

    public static ByteWriter RemoveSkillSet(int objectId, int skillSetId) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.RemoveSkillSet);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(skillSetId);
        return pWriter;
    }

    // 8: the result dialog (0x4f92) for the client's own character: the round (handed to the dialog's setData), the
    // reward item, an int the client keeps but never reads, and the count. The reward row shows only with an item
    // and a count of at least 1.
    public static ByteWriter Result(int objectId, int round, int itemId, int count) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.Result);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(round);
        pWriter.WriteInt(itemId);
        pWriter.WriteInt(0);
        pWriter.WriteInt(count);
        return pWriter;
    }

    public static ByteWriter End(int objectId) {
        var pWriter = Packet.Of(SendOp.HideAndSeek);
        pWriter.WriteByte((byte) Command.End);
        pWriter.WriteInt(objectId);
        return pWriter;
    }
}
