using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Club: what a request may ask before the world server is called.
public static class ClubRules {
    // club_join: the rows read "join a club" (89050036, 89050066, 89050096) with no code. A character has joined
    // once it is a member of an established club: by accepting an invite to one, or when the staged club it agreed
    // to found is established. A staged club is not a club yet and is dropped when a member declines.
    public static bool CountsAsJoin(ClubState state) => state == ClubState.Established;

    // Mode 8: the invite as it was received (i64 club id, club name, inviter name, invitee name), then
    // u8 accept (clubcli.cpp:966-975).
    public static (long ClubId, string ClubName, string Inviter, string Invitee, bool Accept) ReadInviteReply(IByteReader packet) {
        long clubId = packet.ReadLong();
        string clubName = packet.ReadUnicodeString();
        string inviter = packet.ReadUnicodeString();
        string invitee = packet.ReadUnicodeString();
        bool accept = packet.ReadBool();
        return (clubId, clubName, inviter, invitee, accept);
    }

    // Mode 13: i64 club id, i32 additional effect id, i32 effect level (clubcli.cpp:514-519).
    public static (long ClubId, int BuffId, int BuffLevel) ReadBuff(IByteReader packet) {
        long clubId = packet.ReadLong();
        int buffId = packet.ReadInt();
        int buffLevel = packet.ReadInt();
        return (clubId, buffId, buffLevel);
    }

    // The club window sends the effect and level of a table/clubbuff.xml row, and the client sends nothing
    // when the club has that buff already (clubcli.cpp:506-513).
    public static bool BuffAllowed(IEnumerable<(int EffectId, int EffectLevel)> rows, int buffId, int buffLevel, int currentId, int currentLevel) {
        if (buffId == currentId && buffLevel == currentLevel) {
            return false;
        }
        return rows.Any(row => row.EffectId == buffId && row.EffectLevel == buffLevel);
    }

    // The level of a stored buff: the table row with that effect, or no buff when the table has none.
    public static (int BuffId, int BuffLevel) StoredBuff(IEnumerable<(int EffectId, int EffectLevel)> rows, int buffId) {
        foreach ((int effectId, int effectLevel) in rows) {
            if (effectId == buffId && buffId != 0) {
                return (effectId, effectLevel);
            }
        }
        return (0, 0);
    }

    // Only joining needs a free club slot. The client sends a decline with no test of its own
    // (clubcli.cpp:1383-1392), and the decline has to reach the world server to end the invite.
    public static bool ReplyBlockedByLimit(bool accept, int clubCount, int maxClubs) {
        return accept && clubCount >= maxClubs;
    }
}
