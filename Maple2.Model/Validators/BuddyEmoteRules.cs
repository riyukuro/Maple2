using Maple2.Model.Error;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// BuddyEmote: the requests one player's client sends to be passed to the other player.
public static class BuddyEmoteRules {
    // Mode 2: i64 character id, u8 reason (networkservice.cpp:1104-1121, statecoupleemotion.cpp:1349-1402).
    public static (long CharacterId, byte Reason) ReadError(IByteReader packet) {
        long characterId = packet.ReadLong();
        byte reason = packet.ReadByte();
        return (characterId, reason);
    }

    // Only a reason the client has a notice for is passed on.
    public static bool ReasonAllowed(byte reason) {
        return System.Enum.IsDefined((BuddyEmoteError) reason);
    }

    // Mode 6: i32 emote id, i64 partner character id (statecoupleemotion.cpp:924-927).
    public static (int EmoteId, long PartnerId) ReadCancel(IByteReader packet) {
        int emoteId = packet.ReadInt();
        long partnerId = packet.ReadLong();
        return (emoteId, partnerId);
    }

    // The partner's client drops a cancel whose id is not its own partner (networkservice.cpp:1190-1197),
    // so the cancel names the player who cancelled, never the player it is sent to.
    public static long CancelSubject(long cancellerId, long partnerId) {
        return cancellerId;
    }
}
