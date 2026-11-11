using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class BuddyEmoteRulesTests {
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

    [Test]
    public void CancelNamesTheCanceller() {
        Assert.That(BuddyEmoteRules.CancelSubject(5, 77), Is.EqualTo(5));
    }
}
