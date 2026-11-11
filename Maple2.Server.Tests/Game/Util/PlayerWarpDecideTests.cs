using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class PlayerWarpDecideTests {
    // The channel is tested first: the same map id and room id on another channel is another field.
    [TestCase(2, 1, 0, 0, 2000023, 2000023, PlayerWarpUtil.GoToKind.Channel)]
    [TestCase(2, 1, 5, 5, 2000023, 2000023, PlayerWarpUtil.GoToKind.Channel)]
    [TestCase(2, 1, 0, 0, 2000023, 2000062, PlayerWarpUtil.GoToKind.Channel)]
    [TestCase(1, 1, 0, 0, 2000023, 2000023, PlayerWarpUtil.GoToKind.Portal)]
    [TestCase(1, 1, 5, 5, 2000023, 2000023, PlayerWarpUtil.GoToKind.Portal)]
    [TestCase(1, 1, 5, 6, 2000023, 2000023, PlayerWarpUtil.GoToKind.Field)]
    [TestCase(1, 1, 0, 0, 2000023, 2000062, PlayerWarpUtil.GoToKind.Field)]
    public void Decide(int targetChannel, int ownChannel, int targetRoomId, int ownRoomId, int targetMapId, int ownMapId, PlayerWarpUtil.GoToKind expected) {
        Assert.That(PlayerWarpUtil.Decide(targetChannel, ownChannel, targetRoomId, ownRoomId, targetMapId, ownMapId), Is.EqualTo(expected));
    }

    // A GM's recall brings the invited player into the caller's home: the home of the caller's field is the
    // owner the field is prepared with, and another home is another field even with the same map and room id.
    // The other moves to a player (recall scroll, warp command) enter no one else's home.
    [Test]
    public void RecallIntoTheCallersHome() {
        const int homeMap = 62000000;
        const long callerAccount = 11, invitedAccount = 22;

        Assert.That(PlayerWarpUtil.TargetHome(intoHome: true, callerAccount), Is.EqualTo(callerAccount));
        Assert.That(PlayerWarpUtil.TargetHome(intoHome: true, 0), Is.EqualTo(0));
        Assert.That(PlayerWarpUtil.TargetHome(intoHome: false, callerAccount), Is.EqualTo(0));

        // The invited player stands in its own home, the caller in the caller's.
        Assert.That(PlayerWarpUtil.Decide(1, 1, 0, 0, homeMap, homeMap, callerAccount, invitedAccount), Is.EqualTo(PlayerWarpUtil.GoToKind.Field));
        // Both already in the caller's home: a move inside the field.
        Assert.That(PlayerWarpUtil.Decide(1, 1, 3, 3, homeMap, homeMap, callerAccount, callerAccount), Is.EqualTo(PlayerWarpUtil.GoToKind.Portal));
        Assert.That(PlayerWarpUtil.Decide(2, 1, 3, 3, homeMap, homeMap, callerAccount, callerAccount), Is.EqualTo(PlayerWarpUtil.GoToKind.Channel));
    }
}
