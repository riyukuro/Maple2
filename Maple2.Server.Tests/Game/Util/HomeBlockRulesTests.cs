using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HomeBlockRulesTests {
    [Test]
    public void CheckAdd_ReturnsTheClientsErrorCodes() {
        Assert.That(HomeBlockRules.CheckAdd(1, null, false, 0, 100), Is.EqualTo(HomeBlockPacket.Error.NoUser));
        Assert.That(HomeBlockRules.CheckAdd(1, 1, false, 0, 100), Is.EqualTo(HomeBlockPacket.Error.SelfBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, true, 0, 100), Is.EqualTo(HomeBlockPacket.Error.AlreadyBlocked));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, 100, 100), Is.EqualTo(HomeBlockPacket.Error.MaxBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, 99, 100), Is.Null);
    }

    [Test]
    public void MaxBlocks_FallsBackWhenTheConstantIsMissing() {
        Assert.That(HomeBlockRules.MaxBlocks(30), Is.EqualTo(30));
        Assert.That(HomeBlockRules.MaxBlocks(0), Is.EqualTo(HomeBlockRules.DefaultMaxBlocks));
    }

    [Test]
    public void TrimMemo_CutsAtTheInputLength() {
        Assert.That(HomeBlockRules.TrimMemo("note"), Is.EqualTo("note"));
        Assert.That(HomeBlockRules.TrimMemo(new string('a', 300)).Length, Is.EqualTo(50));
    }
}
