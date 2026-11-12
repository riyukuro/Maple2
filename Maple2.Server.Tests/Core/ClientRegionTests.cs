using Maple2.Server.Core.Constants;

namespace Maple2.Server.Tests.Core;

// The region of the client's hello (ResponseVersion) against the server's LOCALE.
public class ClientRegionTests {
    [TestCase((short) 0, "KR")]
    [TestCase((short) 1, "JP")]
    [TestCase((short) 2, "NA")]
    [TestCase((short) 3, "CN")]
    [TestCase((short) 4, "4")]
    [TestCase((short) -1, "-1")]
    [TestCase((short) 300, "300")]
    public void Name(short region, string expected) {
        Assert.That(ClientRegion.Name(region), Is.EqualTo(expected));
    }

    [Test]
    public void Matches_OnlyTheSameRegion() {
        Assert.That(ClientRegion.Matches(2, "NA"), Is.True);
        Assert.That(ClientRegion.Matches(0, "KR"), Is.True);
        Assert.That(ClientRegion.Matches(0, "NA"), Is.False);
        Assert.That(ClientRegion.Matches(4, "TH"), Is.False);
    }
}
