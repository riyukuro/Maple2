using Maple2.PacketLib.Tools;

namespace Maple2.Model.Validators;

// Club: what a request may ask before the world server is called.
public static class ClubRules {
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

    // Only joining needs a free club slot. The client sends a decline with no test of its own
    // (clubcli.cpp:1383-1392), and the decline has to reach the world server to end the invite.
    public static bool ReplyBlockedByLimit(bool accept, int clubCount, int maxClubs) {
        return accept && clubCount >= maxClubs;
    }
}
