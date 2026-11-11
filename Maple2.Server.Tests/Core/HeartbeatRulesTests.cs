using Maple2.Server.Core.Helpers;

namespace Maple2.Server.Tests.Core;

public class HeartbeatRulesTests {
    // The old channel has no session for a player who moved on: its answer says nothing once the record names
    // the new channel.
    [TestCase(1, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(1, -1, false)]
    public void OnlyTheRecordedChannelCounts(int askedChannel, int recordedChannel, bool expected) {
        Assert.That(HeartbeatRules.StillOn(askedChannel, recordedChannel), Is.EqualTo(expected));
    }
}
