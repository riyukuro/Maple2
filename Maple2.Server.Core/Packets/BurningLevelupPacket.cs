using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Core.Packets;

// The burning levelup's EventReward replies, from the login (the character pick) and from the game (the reward and
// the jumping item).
public static class BurningLevelupPacket {
    private enum Command : byte {
        RewardTaken = 0x0f,
        Picked = 0x10,
        JumpingItem = 0x11,
    }

    // The game's answer to the reward claim (EventReward 15): no body; the reward widget updates.
    public static ByteWriter RewardTaken() {
        var pWriter = Packet.Of(SendOp.EventReward);
        pWriter.WriteByte((byte) Command.RewardTaken);
        return pWriter;
    }

    // The login's answer to the pick (EventReward 16): the picked character's id. The character list
    // (CSceneStateMulti) attaches the burning icon to that character when the account's 2000 value of an event is
    // the id, so the 2000 Update goes first.
    public static ByteWriter Picked(long characterId) {
        var pWriter = Packet.Of(SendOp.EventReward);
        pWriter.WriteByte((byte) Command.Picked);
        pWriter.WriteLong(characterId);
        return pWriter;
    }

    // The game's notice of the jumping item given (GameEventBurningLevelupCli::FUN_140ce7250): the event id. The
    // client shows its jumpingItemNotify box with the item when the id is the event's and the text key is set.
    public static ByteWriter JumpingItem(int eventId) {
        var pWriter = Packet.Of(SendOp.EventReward);
        pWriter.WriteByte((byte) Command.JumpingItem);
        pWriter.WriteInt(eventId);
        return pWriter;
    }
}
