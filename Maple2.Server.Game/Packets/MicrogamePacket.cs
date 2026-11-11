using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Microgame 0x0114, read by MicroGameManagerCli (client/ms2/microgamemanagercli.cpp): a short mode, then the fields
// of the mode. See RpsRules for the modes.
public static class MicrogamePacket {
    // Opens the RPS banner (UIRpsEventDialog).
    public static ByteWriter Banner() {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SBanner);
        return pWriter;
    }

    // To the invitee: the request box from the inviter.
    public static ByteWriter Invite(long inviterId) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SInvite);
        pWriter.WriteLong(inviterId);
        return pWriter;
    }

    // To the inviter: the invitee shows the request box; the inviter enters the game state and waits.
    public static ByteWriter InviteSent(long inviteeId) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SInviteSent);
        pWriter.WriteLong(inviteeId);
        return pWriter;
    }

    // To the inviter: the invitee accepted.
    public static ByteWriter Accepted(long inviteeId) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SAccepted);
        pWriter.WriteLong(inviteeId);
        return pWriter;
    }

    // To the inviter: the invitee declined.
    public static ByteWriter Declined(long inviteeId) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SDeclined);
        pWriter.WriteLong(inviteeId);
        return pWriter;
    }

    // To both players: a new round (the card choice).
    public static ByteWriter Round() {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SRound);
        return pWriter;
    }

    // To the invitee: the inviter withdrew the invite.
    public static ByteWriter Cancelled(long inviterId) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SCancelled);
        pWriter.WriteLong(inviterId);
        return pWriter;
    }

    // A failure notice by reason; the client leaves the game state only when actorId is its partner.
    public static ByteWriter Failed(long actorId, byte reason) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SFailed);
        pWriter.WriteLong(actorId);
        pWriter.WriteByte(reason);
        return pWriter;
    }

    // The result for one player: result (draw, win, lose), the player's card, and a card 0..2.
    public static ByteWriter Result(byte result, int ownCard, int pick) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SResult);
        pWriter.WriteByte(result);
        pWriter.WriteByte((byte) ownCard);
        pWriter.WriteByte((byte) pick);
        return pWriter;
    }

    // The items of a claimed reward, shown in the item notify UI.
    public static ByteWriter Rewards(ICollection<RewardItem> items) {
        var pWriter = Packet.Of(SendOp.Microgame);
        pWriter.WriteShort(RpsRules.SRewards);
        pWriter.WriteInt(items.Count);
        foreach (RewardItem item in items) {
            pWriter.WriteInt(item.ItemId);
            pWriter.WriteShort(item.Rarity);
        }
        return pWriter;
    }
}
