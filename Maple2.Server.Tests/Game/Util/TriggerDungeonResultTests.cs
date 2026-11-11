using Maple2.Model.Enum;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// dungeon_fail and dungeon_state: the room record's result.
public class TriggerDungeonResultTests {
    [Test]
    public void DungeonFail_OnlyARoomWithoutAResultFails() {
        Assert.That(TriggerContext.CanFailDungeon(DungeonState.None), Is.True);
        // A cleared room stays cleared, and a failed room sends no second result.
        Assert.That(TriggerContext.CanFailDungeon(DungeonState.Clear), Is.False);
        Assert.That(TriggerContext.CanFailDungeon(DungeonState.Fail), Is.False);
    }

    [Test]
    public void DungeonState_ComparesTheResultName() {
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.None, "Fail"), Is.False);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.Fail, "Fail"), Is.True);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.Clear, "Fail"), Is.False);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.Clear, "Clear"), Is.True);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.None, ""), Is.False);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.Fail, "2"), Is.True);
        Assert.That(TriggerContext.DungeonStateMatches(DungeonState.None, "7"), Is.False);
    }
}
