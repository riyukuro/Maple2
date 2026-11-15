using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Game;

// The comparison conditions npc_hp, dungeon_play_time, dungeon_state and score_board_score, each through its
// comparison helper and through the script mapping. Made-up numbers.
public class TriggerCompareConditionTests : TriggerTestBase {
    [TestCase(CompareType.lower, 51, true)]
    [TestCase(CompareType.lower, 50, false)]
    [TestCase(CompareType.lowerEqual, 50, true)]
    [TestCase(CompareType.lowerEqual, 49, false)]
    [TestCase(CompareType.higher, 50, false)]
    [TestCase(CompareType.higher, 49, true)]
    [TestCase(CompareType.higherEqual, 50, true)]
    [TestCase(CompareType.higherEqual, 51, false)]
    public void NpcHp_Relative(CompareType compare, int value, bool expected) {
        // 500 of 1000 is 50 percent.
        Assert.That(TriggerContext.CompareNpcHp(500, 1000, true, value, compare), Is.EqualTo(expected));
    }

    [Test]
    public void NpcHp_AbsoluteAndEmptyTotal() {
        Assert.That(TriggerContext.CompareNpcHp(500, 1000, false, 400, CompareType.higher), Is.True);
        Assert.That(TriggerContext.CompareNpcHp(500, 1000, false, 500, CompareType.higher), Is.False);
        Assert.That(TriggerContext.CompareNpcHp(500, 1000, false, 500, CompareType.lowerEqual), Is.True);
        // A total of 0 reads as 0 percent, so "lower than 1" holds and "higher than 0" does not.
        Assert.That(TriggerContext.CompareNpcHp(0, 0, true, 1, CompareType.lower), Is.True);
        Assert.That(TriggerContext.CompareNpcHp(0, 0, true, 0, CompareType.higher), Is.False);
    }

    [TestCase(OperatorType.Greater, 9, true)]
    [TestCase(OperatorType.Greater, 10, false)]
    [TestCase(OperatorType.GreaterEqual, 10, true)]
    [TestCase(OperatorType.GreaterEqual, 11, false)]
    [TestCase(OperatorType.Equal, 10, true)]
    [TestCase(OperatorType.Less, 11, true)]
    [TestCase(OperatorType.Less, 10, false)]
    [TestCase(OperatorType.LessEqual, 10, true)]
    [TestCase(OperatorType.LessEqual, 9, false)]
    public void PlayTime_Operators(OperatorType op, int play, bool expected) {
        // 10 whole seconds after the start (10500 ms rounds down).
        Assert.That(TriggerContext.ComparePlayTime(5_000, 15_500, play, op), Is.EqualTo(expected));
    }

    // A room that has not started (no start tick) never passes, and a clock behind the start reads 0.
    [Test]
    public void PlayTime_NotStartedAndBehind() {
        Assert.That(TriggerContext.ComparePlayTime(0, 99_000, 0, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.ComparePlayTime(-1, 99_000, 0, OperatorType.Less), Is.False);
        Assert.That(TriggerContext.ComparePlayTime(9_000, 5_000, 0, OperatorType.Equal), Is.True);
    }

    // Outside a dungeon room the play time test is false.
    [Test]
    public void PlayTime_OutsideDungeonIsFalse() {
        Start();
        Assert.That(Condition("""<condition name="dungeon_play_time" play_seconds="0" operator="GreaterEqual" />"""), Is.False);
    }

    [TestCase("Fail", Maple2.Model.Enum.DungeonState.Fail, true)]
    [TestCase("Fail", Maple2.Model.Enum.DungeonState.Clear, false)]
    [TestCase("Fail", Maple2.Model.Enum.DungeonState.None, false)]
    [TestCase("Clear", Maple2.Model.Enum.DungeonState.Clear, true)]
    [TestCase("clear", Maple2.Model.Enum.DungeonState.Clear, true)]
    [TestCase("", Maple2.Model.Enum.DungeonState.None, false)]
    [TestCase("None", Maple2.Model.Enum.DungeonState.None, true)]
    [TestCase("Bogus", Maple2.Model.Enum.DungeonState.None, false)]
    [TestCase("7", Maple2.Model.Enum.DungeonState.None, false)]
    public void DungeonState_ByName(string name, Maple2.Model.Enum.DungeonState state, bool expected) {
        Assert.That(TriggerContext.DungeonStateMatches(state, name), Is.EqualTo(expected));
    }

    // Outside a dungeon room only the empty name passes.
    [Test]
    public void DungeonState_OutsideDungeon() {
        Start();
        Assert.That(Condition("""<condition name="dungeon_state" check_state="" />"""), Is.True);
        Assert.That(Condition("""<condition name="dungeon_state" check_state="Fail" />"""), Is.False);
    }

    // Before any score is set the board reads as 0 against the threshold; after, as its score.
    [TestCase(OperatorType.GreaterEqual, 0, true)]
    [TestCase(OperatorType.GreaterEqual, 800, true)]
    [TestCase(OperatorType.Greater, 800, true)]
    [TestCase(OperatorType.Equal, 0, true)]
    [TestCase(OperatorType.Equal, 800, false)]
    [TestCase(OperatorType.LessEqual, 800, false)]
    [TestCase(OperatorType.Less, 800, false)]
    public void ScoreBoard_NoScoreYet(OperatorType op, int threshold, bool expected) {
        Assert.That(TriggerContext.CompareScoreBoard(null, threshold, op), Is.EqualTo(expected));
    }

    [TestCase(OperatorType.GreaterEqual, 800, true)]
    [TestCase(OperatorType.GreaterEqual, 801, false)]
    [TestCase(OperatorType.Greater, 799, true)]
    [TestCase(OperatorType.Greater, 800, false)]
    [TestCase(OperatorType.Equal, 800, true)]
    [TestCase(OperatorType.Equal, 799, false)]
    [TestCase(OperatorType.Less, 801, true)]
    [TestCase(OperatorType.LessEqual, 800, true)]
    [TestCase(OperatorType.LessEqual, 799, false)]
    public void ScoreBoard_WithScore(OperatorType op, int threshold, bool expected) {
        Assert.That(TriggerContext.CompareScoreBoard(800, threshold, op), Is.EqualTo(expected));
    }

    // score_board_score reads what score_board_set_score wrote, through the mapping (the operator defaults to
    // GreaterEqual).
    [Test]
    public void ScoreBoard_ThroughScript() {
        Start();
        Action("""<action name="score_board_set_score" score="150" />""");
        Assert.That(Condition("""<condition name="score_board_score" score="100" operator="GreaterEqual" />"""), Is.True);
        Assert.That(Condition("""<condition name="score_board_score" score="100" />"""), Is.True);
        Assert.That(Condition("""<condition name="score_board_score" score="200" operator="GreaterEqual" />"""), Is.False);
        Assert.That(Condition("""<condition name="score_board_score" score="150" operator="Equal" />"""), Is.True);
        Assert.That(Condition("""<condition name="score_board_score" score="150" operator="Greater" />"""), Is.False);
    }
}
