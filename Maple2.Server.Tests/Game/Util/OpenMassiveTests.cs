using System.Collections.Generic;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class OpenMassiveTests {
    [TestCase("20", 20)]
    [TestCase("1", 1)]
    [TestCase("0", 0)]
    [TestCase("-3", 0)]
    [TestCase("x", 0)]
    public void MaxCount_FromParameter(string text, int expected) {
        var parameters = new Dictionary<string, string> { ["fieldID"] = "81000001", ["maxCount"] = text };
        Assert.That(OpenMassive.MaxCount(parameters), Is.EqualTo(expected));
    }

    [Test]
    public void MaxCount_Missing_IsNoLimit() {
        var parameters = new Dictionary<string, string> { ["fieldID"] = "81000001", ["portalDurationTick"] = "30000" };
        Assert.That(OpenMassive.MaxCount(parameters), Is.EqualTo(0));
    }

    [Test]
    public void FullAtMaxCount_ClosesPortal() {
        int maxCount = OpenMassive.MaxCount(new Dictionary<string, string> { ["maxCount"] = "4" });
        Assert.That(RandomRoomRules.Full(3, maxCount), Is.False);
        Assert.That(RandomRoomRules.Full(4, maxCount), Is.True);
    }

    [Test]
    public void NoLimit_NeverFull() {
        Assert.That(RandomRoomRules.Full(500, OpenMassive.MaxCount(new Dictionary<string, string>())), Is.False);
    }
}
