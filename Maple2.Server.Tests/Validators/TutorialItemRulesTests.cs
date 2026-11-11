using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class TutorialItemRulesTests {
    // RequestTutorialItem 0x009C has no body.
    [Test]
    public void EmptyBody() {
        var packet = new ByteReader(new byte[] { 0x9C, 0x00 }, 2);
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(0, new[] { 1 }, 1)]
    [TestCase(1, new[] { 1 }, 0)]
    [TestCase(0, new[] { 10 }, 10)]
    [TestCase(10, new[] { 10 }, 0)] // one stack of 10 is 10 pieces, not 1
    [TestCase(4, new[] { 10 }, 6)]
    [TestCase(1, new[] { 1, 1 }, 1)] // two rows of the same item
    [TestCase(50, new[] { 10 }, 0)]
    [TestCase(0, new[] { -3, 2 }, 2)]
    public void MissingPieces(int owned, int[] counts, int expected) {
        Assert.That(TutorialItemRules.Missing(owned, counts), Is.EqualTo(expected));
    }
}
