using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class ScoreBoardTests {
    [Test]
    public void ShadowGauge_IsBossGauge() {
        Assert.That(ScoreBoard.Kind("ShadowGauge", 800), Is.EqualTo((byte) 1));
    }

    [Test]
    public void ShadowGauge_WithoutMost_IsNone() {
        Assert.That(ScoreBoard.Kind("ShadowGauge", 0), Is.Null);
    }

    [Test]
    public void TopCenter_IsTopCenterScore() {
        Assert.That(ScoreBoard.Kind("ScoreBoardTopCenter", 0), Is.EqualTo((byte) 2));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("ScoreBoardTopRight")]
    public void OtherTypes_AreNone(string? type) {
        Assert.That(ScoreBoard.Kind(type, 240), Is.Null);
    }
}
