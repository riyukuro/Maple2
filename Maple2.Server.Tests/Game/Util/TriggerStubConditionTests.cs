using System.Xml;
using Maple2.Model.Enum;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using TriggerConditions = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// score_board_score, dungeon_play_time, check_same_user_tag and is_playing_maple_survival. Made-up values.
public class TriggerStubConditionTests {
    private static ICondition Condition(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        XmlElement node = document.DocumentElement!;
        return TriggerFunctionMapping.ConditionMap[node.GetAttribute("name")](node.Attributes);
    }

    [Test]
    public void ScoreBoardScore_ReadsTheIngestOperator() {
        var condition = (TriggerConditions.ScoreBoardScore) Condition("""<condition name="score_board_score" score="100" operator="Less" />""");
        Assert.That(condition.Operator, Is.EqualTo(OperatorType.Less));
        var old = (TriggerConditions.ScoreBoardScore) Condition("""<condition name="score_board_score" score="100" compare_op="Equal" />""");
        Assert.That(old.Operator, Is.EqualTo(OperatorType.Equal));
        Assert.That(TriggerContext.CompareUserValue(0, 100, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.CompareUserValue(120, 100, OperatorType.GreaterEqual), Is.True);
    }

    [Test]
    public void ScoreBoardScore_PassesUntilAScriptSetsTheScore() {
        // No score source feeds the board: a field whose scripts never call score_board_set_score keeps the
        // old test (the threshold compared with 0), so a battle waiting on 800 points still ends.
        Assert.That(TriggerContext.CompareScoreBoard(null, 800, OperatorType.GreaterEqual), Is.True);
        Assert.That(TriggerContext.CompareScoreBoard(null, 800, OperatorType.Less), Is.False);
        Assert.That(TriggerContext.CompareScoreBoard(null, 800, OperatorType.LessEqual), Is.False);
        Assert.That(TriggerContext.CompareScoreBoard(null, 0, OperatorType.Equal), Is.True);
        // After score_board_set_score="0" (the bonus maps' start), the settlement's score test is real.
        Assert.That(TriggerContext.CompareScoreBoard(0, 18000, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.CompareScoreBoard(0, 18000, OperatorType.Less), Is.True);
        Assert.That(TriggerContext.CompareScoreBoard(18000, 18000, OperatorType.GreaterEqual), Is.True);
    }

    [Test]
    public void DungeonPlayTime_KeepsItsOperatorAndCountsFromTheStart() {
        var condition = (TriggerConditions.DungeonPlayTime) Condition("""<condition name="dungeon_play_time" play_seconds="70" operator="LessEqual" />""");
        Assert.That(condition.Operator, Is.EqualTo(OperatorType.LessEqual));
        var plain = (TriggerConditions.DungeonPlayTime) Condition("""<condition name="dungeon_play_time" play_seconds="300" />""");
        Assert.That(plain.Operator, Is.EqualTo(OperatorType.GreaterEqual));

        Assert.That(TriggerContext.ComparePlayTime(0, 500_000, 300, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.ComparePlayTime(1_000, 300_999, 300, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.ComparePlayTime(1_000, 301_000, 300, OperatorType.GreaterEqual), Is.True);
        Assert.That(TriggerContext.ComparePlayTime(1_000, 60_000, 70, OperatorType.LessEqual), Is.True);
    }

    [Test]
    public void CheckSameUserTag_NeedsOnePlayerAndOneTag() {
        Assert.That(TriggerContext.SameUserTag([]), Is.False);
        Assert.That(TriggerContext.SameUserTag([2]), Is.True);
        Assert.That(TriggerContext.SameUserTag([2, 2]), Is.True);
        Assert.That(TriggerContext.SameUserTag([1, 2]), Is.False);
    }

    [Test]
    public void IsPlayingMapleSurvival_IsFalseUnlessNegated() {
        Assert.That(TriggerContext.MapleSurvivalResult(false), Is.False);
        Assert.That(TriggerContext.MapleSurvivalResult(true), Is.True);
    }
}
