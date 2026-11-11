using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class AttendanceRulesTests {
    [Test]
    public void EarlyParticipationBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(1234);
        writer.WriteLong(0x1122334455667788);
        var reader = new ByteReader(writer.ToArray(), 0);
        (int eventId, long couponUid) = AttendanceRules.ReadEarlyParticipation(reader);
        Assert.That(eventId, Is.EqualTo(1234));
        Assert.That(couponUid, Is.EqualTo(0x1122334455667788));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void SelectedEventBytes() {
        var writer = new ByteWriter();
        writer.WriteInt(77);
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(AttendanceRules.ReadSelectedEvent(reader), Is.EqualTo(77));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Three rewards: claims 1 to 3 give rewards 0 to 2, a fourth claim gives nothing.
    [TestCase(0, 3, 0)]
    [TestCase(1, 3, 1)]
    [TestCase(2, 3, 2)]
    [TestCase(3, 3, -1)]
    [TestCase(-1, 3, -1)]
    [TestCase(0, 0, -1)]
    public void RewardIndex(int claimed, int count, int expected) {
        Assert.That(AttendanceRules.RewardIndex(claimed, count), Is.EqualTo(expected));
    }
}
