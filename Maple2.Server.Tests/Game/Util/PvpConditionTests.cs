using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The pvp_* conditions, shaped like the trophies 21300003-21300041 and the rank duel quests: the code is the map of
// the fight, the target the win's lead, seconds, grade or effect.
public class PvpConditionTests {
    private const int RedArena = 65000001;
    private const int Mine = 65000002;
    private const int TreasureIsland = 65000003;

    private static ConditionMetadata.Parameters Codes(params int[] maps) => new(Integers: maps);

    [TestCase(ConditionType.pvp_kill, RedArena, true)]
    [TestCase(ConditionType.pvp_kill, Mine, false)]
    [TestCase(ConditionType.pvp_win, RedArena, true)]
    [TestCase(ConditionType.pvp_die, TreasureIsland, false)]
    public void Code_IsTheMapOfTheFight(ConditionType type, int mapId, bool expected) {
        var condition = new ConditionMetadata(type, 10, Codes(RedArena), null);
        Assert.That(condition.Check(null!, codeLong: mapId), Is.EqualTo(expected));
    }

    // The rank duel quests name the ten duel maps as a range.
    [TestCase(65010001, true)]
    [TestCase(65010010, true)]
    [TestCase(65010011, false)]
    [TestCase(RedArena, false)]
    public void Participation_CountsTheRankDuelMapRange(int mapId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pvp_participation, 1,
            new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(65010001, 65010010)), null);
        Assert.That(condition.Check(null!, codeLong: mapId), Is.EqualTo(expected));
    }

    // A row without a code (21300014) counts on every PvP map.
    [TestCase(RedArena)]
    [TestCase(65010004)]
    public void Participation_WithoutCode_CountsAnywhere(int mapId) {
        var condition = new ConditionMetadata(ConditionType.pvp_participation, 1, null, null);
        Assert.That(condition.Check(null!, codeLong: mapId), Is.True);
    }

    [TestCase(0, true)]
    [TestCase(50, true)]
    [TestCase(51, false)]
    public void WinScore_IsALeadOfAtMostTheTarget(int lead, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pvp_win_score, 1, Codes(TreasureIsland), new ConditionMetadata.Parameters(Integers: [50]));
        Assert.That(condition.Check(null!, targetLong: lead, codeLong: TreasureIsland), Is.EqualTo(expected));
    }

    [TestCase(299, true)]
    [TestCase(300, true)]
    [TestCase(301, false)]
    public void WinTime_IsAtMostTheTargetSeconds(int seconds, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pvp_win_time, 1, Codes(TreasureIsland), new ConditionMetadata.Parameters(Integers: [300]));
        Assert.That(condition.Check(null!, targetLong: seconds, codeLong: TreasureIsland), Is.EqualTo(expected));
    }

    [TestCase(2, false)]
    [TestCase(3, true)]
    [TestCase(7, true)]
    public void WinWithGrade_HoldsAtTheGradeOrHigher(int grade, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pvp_win_with_grade, 1, null, new ConditionMetadata.Parameters(Integers: [3]));
        Assert.That(condition.Check(null!, targetLong: grade), Is.EqualTo(expected));
    }

    [TestCase(90001010, true)]
    [TestCase(90001009, false)]
    public void WinWithBuff_IsTheEffectHeld(int effectId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.pvp_win_with_buff, 1, null, new ConditionMetadata.Parameters(Integers: [90001010]));
        Assert.That(condition.Check(null!, targetLong: effectId), Is.EqualTo(expected));
    }

    [Test]
    public void TeamWin_RaisesTheWinLeadAndSeconds() {
        var updates = PvpConditions.TeamWinUpdates(40, 299_999);
        Assert.That(updates, Is.EqualTo(new[] {
            new PvpConditions.Update(ConditionType.pvp_win),
            new PvpConditions.Update(ConditionType.pvp_win_score, 40),
            new PvpConditions.Update(ConditionType.pvp_win_time, 299),
        }));
    }

    [Test]
    public void DuelWin_RaisesPerfectEachEffectAndTheGrade() {
        var updates = PvpConditions.DuelWinUpdates(true, [90001010, 90001010, 70000040], 4);
        Assert.That(updates, Is.EqualTo(new[] {
            new PvpConditions.Update(ConditionType.pvp_win),
            new PvpConditions.Update(ConditionType.pvp_win_perfect),
            new PvpConditions.Update(ConditionType.pvp_win_with_buff, 90001010),
            new PvpConditions.Update(ConditionType.pvp_win_with_buff, 70000040),
            new PvpConditions.Update(ConditionType.pvp_win_with_grade, 4),
        }));
    }

    [Test]
    public void DuelWin_NotSaved_HasNoGrade() {
        var updates = PvpConditions.DuelWinUpdates(false, [], null);
        Assert.That(updates, Is.EqualTo(new[] { new PvpConditions.Update(ConditionType.pvp_win) }));
    }
}
