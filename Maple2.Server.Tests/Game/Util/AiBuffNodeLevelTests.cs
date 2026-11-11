using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Game.Util;

// An AI buff node without a level (parsed as 0) adds the effect's first level.
public class AiBuffNodeLevelTests {
    [Test]
    public void MissingLevelIsLevelOne() {
        Assert.That(AiState.BuffNodeLevel(0), Is.EqualTo(1));
    }

    [TestCase((short) 1)]
    [TestCase((short) 3)]
    public void GivenLevelIsKept(short level) {
        Assert.That(AiState.BuffNodeLevel(level), Is.EqualTo(level));
    }
}
