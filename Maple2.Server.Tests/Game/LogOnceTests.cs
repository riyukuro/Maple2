using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game;

// Warnings that a data gap would repeat on every tick or for every id are logged once.
public class LogOnceTests {
    [Test]
    public void MissingSkill_IsFirstOncePerNpcScriptAndIndex() {
        Assert.That(AiState.FirstMissingSkill(99000001, "AI_LogOnceTest.xml", 3), Is.True);
        Assert.That(AiState.FirstMissingSkill(99000001, "AI_LogOnceTest.xml", 3), Is.False);
        Assert.That(AiState.FirstMissingSkill(99000001, "AI_LogOnceTest.xml", 4), Is.True);
        Assert.That(AiState.FirstMissingSkill(99000002, "AI_LogOnceTest.xml", 3), Is.True);
        Assert.That(AiState.FirstMissingSkill(99000001, "AI_LogOnceOther.xml", 3), Is.True);
    }

    [Test]
    public void MeshIdList_FoldsRuns() {
        Assert.That(TriggerContext.MeshIdList([6009, 6018, 6019, 6020, 6051]), Is.EqualTo("6009, 6018-6020, 6051"));
        Assert.That(TriggerContext.MeshIdList([5]), Is.EqualTo("5"));
        Assert.That(TriggerContext.MeshIdList([]), Is.EqualTo(""));
    }
}
