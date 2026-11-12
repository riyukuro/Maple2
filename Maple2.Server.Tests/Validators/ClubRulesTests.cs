using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ClubRulesTests {
    // club_join: a member of an established club has joined one; a staged club is not a club yet.
    [TestCase(ClubState.Established, true)]
    [TestCase(ClubState.Staged, false)]
    public void ClubJoin_CountsAnEstablishedClub(ClubState state, bool expected) {
        Assert.That(ClubRules.CountsAsJoin(state), Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void InviteReplyBytes(bool accept) {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteUnicodeString("club");
        writer.WriteUnicodeString("leader");
        writer.WriteUnicodeString("me");
        writer.WriteBool(accept);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long clubId, string clubName, string inviter, string invitee, bool read) = ClubRules.ReadInviteReply(reader);
        Assert.That(clubId, Is.EqualTo(0x1122334455667788));
        Assert.That(clubName, Is.EqualTo("club"));
        Assert.That(inviter, Is.EqualTo("leader"));
        Assert.That(invitee, Is.EqualTo("me"));
        Assert.That(read, Is.EqualTo(accept));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // A decline is never held back by the club limit; an accept is.
    [TestCase(true, 2, 3, false)]
    [TestCase(true, 3, 3, true)]
    [TestCase(true, 4, 3, true)]
    [TestCase(false, 3, 3, false)]
    [TestCase(false, 4, 3, false)]
    public void Limit(bool accept, int count, int max, bool blocked) {
        Assert.That(ClubRules.ReplyBlockedByLimit(accept, count, max), Is.EqualTo(blocked));
    }
}
