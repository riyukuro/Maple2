using System.Threading;
using Maple2.Server.Core.Helpers;
using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// Two Game servers started together: the first one's channel is pending until its Game server listens, which
// takes some seconds (it loads its data first). The World server's health check of it gets no answer meanwhile.
public class ChannelStartTests {
    [TestCase(true, 0L, true)]
    [TestCase(false, 0L, false)]
    [TestCase(false, ChannelRegisterRules.StartGraceMs - 1, false)]
    [TestCase(false, ChannelRegisterRules.StartGraceMs, true)]
    public void AnUnansweredCheckEndsOnlyAServedOrLateChannel(bool served, long pendingMs, bool expected) {
        Assert.That(ChannelRegisterRules.UnansweredCheckEndsChannel(served, pendingMs), Is.EqualTo(expected));
    }

    // The second Game server gets an id of its own, and the first one keeps its id when it checks its link.
    // The gRPC address is one nothing listens on, so the health checks of channel 1 get no answer.
    [Test]
    public void ASecondGameServerDoesNotTakeAStartingChannel() {
        var lookup = new ChannelClientLookup();
        (_, _, int first) = lookup.FindOrCreateChannelByIp("127.0.0.1", "127.0.0.2", false, "first");
        Assert.That(first, Is.EqualTo(1));

        // The monitor's first check runs at once and fails at once (connection refused).
        Thread.Sleep(1500);

        (ushort secondPort, _, int second) = lookup.FindOrCreateChannelByIp("127.0.0.1", "127.0.0.2", false, "second");
        Assert.That(second, Is.EqualTo(2));
        Assert.That(secondPort, Is.EqualTo(Maple2.Server.Core.Constants.Target.BaseGamePort + 2));

        (_, _, int again, bool known) = lookup.Register("127.0.0.1", "127.0.0.2", false, 1, "first");
        Assert.That((again, known), Is.EqualTo((1, true)));
    }
}
