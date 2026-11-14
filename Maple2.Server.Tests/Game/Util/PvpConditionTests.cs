using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
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

    // The rank duel quests list the ten duel maps; a range code counts the same.
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

    // 90000006 counts the matches of season 1; 21300012 (no target) those of every season.
    [TestCase(1, true)]
    [TestCase(9, false)]
    public void GuildChampionship_IsTheSeasonOfTheMatch(int seasonId, bool expected) {
        var seasonRow = new ConditionMetadata(ConditionType.guild_championship, 20, null, new ConditionMetadata.Parameters(Integers: [1]));
        var anySeason = new ConditionMetadata(ConditionType.guild_championship, 1, null, null);
        Assert.Multiple(() => {
            Assert.That(seasonRow.Check(null!, targetLong: seasonId), Is.EqualTo(expected));
            Assert.That(anySeason.Check(null!, targetLong: seasonId), Is.True);
        });
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
        var updates = PvpConditions.DuelWinUpdates(RankDuelWinBy.Death, true, [90001010, 90001010, 70000040], 4);
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
        var updates = PvpConditions.DuelWinUpdates(RankDuelWinBy.Death, false, [], null);
        Assert.That(updates, Is.EqualTo(new[] { new PvpConditions.Update(ConditionType.pvp_win) }));
    }

    // A walkover (the opponent never came, or left before the fight) gives no condition: it would be a farmable
    // perfect win.
    [Test]
    public void DuelWin_Walkover_RaisesNothing() {
        var late = new RankDuelMatchState(1, 2, 0);
        late.Enter(1, 0);
        late.Update(RankDuelMatchState.JoinMs);
        Assert.That(late.WinBy, Is.EqualTo(RankDuelWinBy.Walkover));

        var gone = new RankDuelMatchState(1, 2, 0);
        gone.Enter(1, 0);
        gone.Enter(2, 0);
        gone.Leave(2, 1_000);
        Assert.That(gone.WinBy, Is.EqualTo(RankDuelWinBy.Walkover));

        Assert.That(PvpConditions.DuelWinUpdates(RankDuelWinBy.Walkover, true, [90001010], 4), Is.Empty);
    }

    // An opponent that leaves during the fight: the win only, no perfect, buff or grade.
    [Test]
    public void DuelWin_OpponentLeftTheFight_IsTheWinOnly() {
        var duel = Fighting();
        duel.Leave(2, 10_000);
        Assert.That(duel.WinBy, Is.EqualTo(RankDuelWinBy.LeftFight));
        Assert.That(PvpConditions.DuelWinUpdates(duel.WinBy, true, [90001010], 4),
            Is.EqualTo(new[] { new PvpConditions.Update(ConditionType.pvp_win) }));
    }

    [Test]
    public void DuelWin_ByDeath_IsWinBy_Death() {
        var duel = Fighting();
        duel.Death(2, 10_000);
        Assert.That(duel.WinBy, Is.EqualTo(RankDuelWinBy.None), "round 1 of a best of 3");
        duel.Update(10_000 + RankDuelMatchState.RoundBreakMs);
        duel.Update(10_000 + RankDuelMatchState.RoundBreakMs + RankDuelMatchState.CountdownMs);
        duel.Death(2, 20_000);
        Assert.That(duel.WinBy, Is.EqualTo(RankDuelWinBy.Death));
    }

    // A win on more rounds when the last round ran out: the win only, no perfect, buff or grade.
    [Test]
    public void DuelWin_OnRounds_IsTheWinOnly() {
        Assert.That(PvpConditions.DuelWinUpdates(RankDuelWinBy.Rounds, true, [90001010], 4),
            Is.EqualTo(new[] { new PvpConditions.Update(ConditionType.pvp_win) }));
    }

    private static RankDuelMatchState Fighting() {
        var duel = new RankDuelMatchState(1, 2, 0);
        duel.Enter(1, 0);
        duel.Enter(2, 0);
        duel.Update(RankDuelMatchState.CountdownMs);
        Assert.That(duel.Phase, Is.EqualTo(RankDuelPhase.Fighting));
        return duel;
    }

    // A one-on-one death is a kill only for a hit by the other fighter within the kill-credit window.
    [Test]
    public void KillCredit_NeedsAHitWithinTheWindow() {
        var credit = new KillCredit();
        Assert.That(credit.Killer(2, 1_000), Is.EqualTo(0), "no hit: a fall");

        credit.Hit(1, 2, 1_000);
        Assert.That(credit.Killer(2, 1_000 + PvpZoneState.KillCreditMs), Is.EqualTo(1));
        Assert.That(credit.Killer(2, 1_000 + PvpZoneState.KillCreditMs), Is.EqualTo(0), "the hit is used up");

        credit.Hit(1, 2, 1_000);
        Assert.That(credit.Killer(2, 1_000 + PvpZoneState.KillCreditMs + 1), Is.EqualTo(0), "too old");

        credit.Hit(2, 2, 1_000);
        Assert.That(credit.Killer(2, 1_000), Is.EqualTo(0), "its own hit");
    }
}
