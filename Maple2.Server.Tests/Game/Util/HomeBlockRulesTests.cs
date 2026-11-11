using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HomeBlockRulesTests {
    [Test]
    public void CheckAdd_ReturnsTheClientsErrorCodes() {
        Assert.That(HomeBlockRules.CheckAdd(1, null, false, 0), Is.EqualTo(HomeBlockPacket.Error.NoUser));
        Assert.That(HomeBlockRules.CheckAdd(1, 1, false, 0), Is.EqualTo(HomeBlockPacket.Error.SelfBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, true, 0), Is.EqualTo(HomeBlockPacket.Error.AlreadyBlocked));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, HomeBlockRules.MaxBlocks), Is.EqualTo(HomeBlockPacket.Error.MaxBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, HomeBlockRules.MaxBlocks - 1), Is.Null);
    }

    [Test]
    public void TrimMemo_CutsAtTheColumnLength() {
        Assert.That(HomeBlockRules.TrimMemo("note"), Is.EqualTo("note"));
        Assert.That(HomeBlockRules.TrimMemo(new string('a', 300)).Length, Is.EqualTo(HomeBlockRules.MaxMemoLength));
    }
}
