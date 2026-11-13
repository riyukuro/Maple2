using System.Numerics;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class BuddyEmotePacket {
    private enum Command : byte {
        Invite = 0,
        InviteConfirm = 1,
        Error = 2,
        Accept = 3,
        Decline = 4,
        Start = 5,
        Cancel = 6,
    }

    public static ByteWriter Invite(int emoteId, Player sender) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Invite);
        pWriter.WriteInt(emoteId);
        pWriter.WriteLong(sender.Character.Id);
        pWriter.WriteUnicodeString(sender.Character.Name);

        return pWriter;
    }

    // You sent {0} an invitation to participate in a buddy emote.
    public static ByteWriter InviteConfirm(long receiverId) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.InviteConfirm);
        pWriter.WriteLong(receiverId);

        return pWriter;
    }

    public static ByteWriter Error(BuddyEmoteError error) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Error);
        pWriter.WriteByte((byte) error);

        return pWriter;
    }

    public static ByteWriter Accept(int emoteId, long receiverId) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Accept);
        pWriter.WriteInt(emoteId);
        pWriter.WriteLong(receiverId);

        return pWriter;
    }

    public static ByteWriter Decline(int emoteId, long receiverId) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Decline);
        pWriter.WriteInt(emoteId);
        pWriter.WriteLong(receiverId);

        return pWriter;
    }

    // Client: networkservice.cpp:1150 (mode 5). The last int goes to CEffectService::SetTick, the seed the emote's
    // random effects are drawn from, so both players must get the same value.
    public static ByteWriter Start(int emoteId, long senderId, long receiverId, in Vector3 senderPosition, in Vector3 senderRotation, int effectSeed) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Start);
        pWriter.WriteInt(emoteId);
        pWriter.WriteLong(senderId);
        pWriter.WriteLong(receiverId);
        WriteVector(pWriter, senderPosition);
        WriteVector(pWriter, senderRotation);
        pWriter.WriteInt(effectSeed);

        return pWriter;
    }

    public static ByteWriter Cancel(int emoteId, long characterId) {
        var pWriter = Packet.Of(SendOp.BuddyEmote);
        pWriter.WriteByte((byte) Command.Cancel);
        pWriter.WriteInt(emoteId);
        pWriter.WriteLong(characterId);

        return pWriter;
    }

    private static void WriteVector(ByteWriter pWriter, in Vector3 vector) {
        pWriter.WriteFloat(vector.X);
        pWriter.WriteFloat(vector.Y);
        pWriter.WriteFloat(vector.Z);
    }
}
