using Maple2.Model.Error;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// The couple emote invites one player has received and not answered yet. Modes 1 to 4 are all sent by
// the invited player about an invite (networkservice.cpp:1104-1127, statecoupleemotion.cpp:1349-1415,
// uimessagebox.cpp:2273-2284), so each is passed on only to a player found here.
public sealed class BuddyEmoteInvites {
    // The client shows one box per invite; a few may be open at once.
    public const int MaxPending = 8;

    private readonly System.Collections.Generic.List<(long InviterId, int EmoteId)> pending = [];

    // Mode 0, on the invited player's side. A new invite of the same player replaces the old one.
    public void Add(long inviterId, int emoteId) {
        lock (pending) {
            pending.RemoveAll(entry => entry.InviterId == inviterId);
            if (pending.Count >= MaxPending) {
                pending.RemoveAt(0);
            }
            pending.Add((inviterId, emoteId));
        }
    }

    // Mode 1: the box is up. The invite stays open.
    public bool Has(long inviterId) {
        lock (pending) {
            return pending.Exists(entry => entry.InviterId == inviterId);
        }
    }

    // Modes 2 and 4: the invite ends, whatever emote it was for.
    public bool Take(long inviterId) {
        lock (pending) {
            return pending.RemoveAll(entry => entry.InviterId == inviterId) > 0;
        }
    }

    // Mode 3: the invite ends, and the accepted emote has to be the invited one.
    public bool Take(long inviterId, int emoteId) {
        lock (pending) {
            return pending.RemoveAll(entry => entry.InviterId == inviterId && entry.EmoteId == emoteId) > 0;
        }
    }
}

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

    // Modes 0 and 4: i32 emote id, i64 character id (stateskill.cpp:1983, uimessagebox.cpp:2279-2283).
    public static (int EmoteId, long CharacterId) ReadEmoteAndPlayer(IByteReader packet) {
        int emoteId = packet.ReadInt();
        long characterId = packet.ReadLong();
        return (emoteId, characterId);
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
