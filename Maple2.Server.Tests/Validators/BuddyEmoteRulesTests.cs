using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BuddyEmoteRulesTests {
    [Test]
    public void EmoteAndPlayerBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(90200011);
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int emoteId, long characterId) = BuddyEmoteRules.ReadEmoteAndPlayer(reader);
        Assert.That(emoteId, Is.EqualTo(90200011));
        Assert.That(characterId, Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void AcceptWithoutInvite() {
        var invites = new BuddyEmoteInvites();
        Assert.That(invites.Has(7), Is.False);
        Assert.That(invites.Take(7, 90200011), Is.False);
        Assert.That(invites.Take(7), Is.False);
    }

    [Test]
    public void AcceptWithInvite() {
        var invites = new BuddyEmoteInvites();
        invites.Add(7, 90200011);
        Assert.That(invites.Has(7), Is.True);
        Assert.That(invites.Has(8), Is.False);
        // Another player or another emote is not the invite.
        Assert.That(invites.Take(8, 90200011), Is.False);
        Assert.That(invites.Take(7, 90200012), Is.False);
        Assert.That(invites.Take(7, 90200011), Is.True);
        // One invite, one accept.
        Assert.That(invites.Take(7, 90200011), Is.False);
        Assert.That(invites.Has(7), Is.False);
    }

    [Test]
    public void ErrorOrDeclineEndsTheInvite() {
        var invites = new BuddyEmoteInvites();
        invites.Add(7, 90200011);
        Assert.That(invites.Take(7), Is.True);
        Assert.That(invites.Take(7), Is.False);
        Assert.That(invites.Take(7, 90200011), Is.False);
    }

    [Test]
    public void NewInviteReplacesAndTheListIsBounded() {
        var invites = new BuddyEmoteInvites();
        invites.Add(7, 1);
        invites.Add(7, 2);
        Assert.That(invites.Take(7, 1), Is.False);
        Assert.That(invites.Take(7, 2), Is.True);

        for (int i = 0; i <= BuddyEmoteInvites.MaxPending; i++) {
            invites.Add(100 + i, 1);
        }
        Assert.That(invites.Has(100), Is.False);
        Assert.That(invites.Has(101), Is.True);
        Assert.That(invites.Has(100 + BuddyEmoteInvites.MaxPending), Is.True);
    }

    [Test]
    public void ErrorBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteByte(8);
        var reader = new ByteReader(writer.ToArray(), 0);
        (long characterId, byte reason) = BuddyEmoteRules.ReadError(reader);
        Assert.That(characterId, Is.EqualTo(0x1122334455667788));
        Assert.That(reason, Is.EqualTo(8));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [TestCase(0, true)]
    [TestCase(8, true)]
    [TestCase(200, false)]
    public void Reason(byte reason, bool expected) {
        Assert.That(BuddyEmoteRules.ReasonAllowed(reason), Is.EqualTo(expected));
    }

    [Test]
    public void CancelBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(90200001);
        writer.WriteLong(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int emoteId, long partnerId) = BuddyEmoteRules.ReadCancel(reader);
        Assert.That(emoteId, Is.EqualTo(90200001));
        Assert.That(partnerId, Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Mode 6 from the inviter ends its invite: the invited player's later accept or decline finds nothing.
    // An invite of another player stays.
    [Test]
    public void CancelEndsTheInvite() {
        var invites = new BuddyEmoteInvites();
        invites.Add(5, 90200001);
        invites.Add(6, 90200002);
        Assert.That(invites.Cancel(5), Is.True);
        Assert.That(invites.Has(5), Is.False);
        Assert.That(invites.Take(5, 90200001), Is.False);
        Assert.That(invites.Take(5), Is.False);
        Assert.That(invites.Cancel(5), Is.False);
        Assert.That(invites.Take(6, 90200002), Is.True);
    }

    [Test]
    public void CancelNamesTheCanceller() {
        Assert.That(BuddyEmoteRules.CancelSubject(5, 77), Is.EqualTo(5));
    }
}
