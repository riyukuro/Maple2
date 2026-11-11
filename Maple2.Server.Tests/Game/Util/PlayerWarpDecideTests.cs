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
}
