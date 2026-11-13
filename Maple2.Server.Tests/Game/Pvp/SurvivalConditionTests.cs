using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Pvp;

// The Maple Survival trophies (21300042-21300099): one match's play in SurvivalGameState, the conditions its result
// raises (SurvivalConditions) and how the trophy rows' codes and targets take them.
public class SurvivalConditionTests {
    private const int Jab = 90000772;
    private const int Bomb = 90000729;

    [Test]
    public void Fall_CountsTheKillingSkillAndADoubleKillInOneTick() {
        var game = new SurvivalGameState([1, 2, 3, 4]);
        game.Hit(1, 2, 100, Jab);
        game.Hit(1, 3, 100, Jab);
        Assert.That(game.Fall(2, 150, out long killer, out int skillId, out bool doubleKill), Is.True);
        Assert.That((killer, skillId, doubleKill), Is.EqualTo((1L, Jab, false)));
        game.Fall(3, 160, out _, out _, out doubleKill);
        Assert.That(doubleKill, Is.True);
        Assert.That(game.Get(1)!.KillSkills[Jab], Is.EqualTo(2));
    }

    [Test]
    public void Fall_HitsInDifferentTicksAreNoDoubleKill() {
        var game = new SurvivalGameState([1, 2, 3, 4]);
        game.Hit(1, 2, 100, Jab);
        game.Hit(1, 3, 101, Bomb);
        game.Fall(2, 200, out _, out _, out _);
        game.Fall(3, 200, out _, out _, out bool doubleKill);
        Assert.That(doubleKill, Is.False);
        Assert.That(game.Get(1)!.KillSkills.Keys, Is.EquivalentTo(new[] { Jab, Bomb }));
    }

    [Test]
    public void Play_CountsOnlyWhilePlaying() {
        var game = new SurvivalGameState([1, 2, 3]);
        game.NpcKill(2);
        game.Break(2);
        game.Interact(2);
        game.ItemGot(2, 20000733, 3);
        game.ItemGot(2, 20000733, 2);
        game.Fall(2, 1_000, out _);
        game.NpcKill(2);
        game.ItemGot(2, 20000733, 5);
        SurvivalGameState.Entry entry = game.Get(2)!;
        Assert.That((entry.NpcKills, entry.Breakables, entry.Interacted, entry.Items[20000733]), Is.EqualTo((1, 1, true, 5)));
    }

    // The winner's result: kills, rank 1, each skill's kills, the seconds from the start to the game's end and the
    // win trophies; a fallen loser's ends at its fall and has no win trophies.
    [Test]
    public void Result_RaisesTheMatchCountsAndTheWins() {
        var game = new SurvivalGameState([1, 2, 3]);
        game.Hit(1, 2, 5_000, Bomb);
        game.Fall(2, 5_000, out _);
        game.Hit(1, 3, 9_000, Bomb);
        game.Fall(3, 9_500, out _);
        Assert.That(game.Winner, Is.EqualTo(1));

        List<SurvivalConditions.Raise> winner = SurvivalConditions.Result(game.Get(1)!, 1, 1_000, 82000001);
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_kill, Target: 2)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_rank_with_kill, Target: 2, Code: 1)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_kill_use_skill, Target: 2, Code: Bomb)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survive_map, Counter: 8, Code: 82000001)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_win_use_one_skill, Code: Bomb)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_win_without_interact)));
        Assert.That(winner, Does.Contain(new SurvivalConditions.Raise(ConditionType.survival_win_without_npckill)));

        List<SurvivalConditions.Raise> loser = SurvivalConditions.Result(game.Get(2)!, game.Get(2)!.Rank, 1_000, 82000001);
        Assert.That(loser, Does.Contain(new SurvivalConditions.Raise(ConditionType.survive_map, Counter: 4, Code: 82000001)));
        Assert.That(loser.Select(raise => raise.Type), Has.None.EqualTo(ConditionType.survival_win_without_npckill));
    }

    // A win with kills by two skills, or a kill without a known skill, is no one-skill win; a chest or a monster takes
    // the other wins away.
    [Test]
    public void Result_NoOneSkillWinWithMixedKills() {
        var game = new SurvivalGameState([1, 2, 3]);
        game.Hit(1, 2, 100, Bomb);
        game.Fall(2, 100, out _);
        game.Interact(1);
        game.NpcKill(1);
        game.Hit(1, 3, 200);
        game.Fall(3, 200, out _);
        List<ConditionType> types = SurvivalConditions.Result(game.Get(1)!, 1, 0, 82000001).Select(raise => raise.Type).ToList();
        Assert.That(types, Has.None.EqualTo(ConditionType.survival_win_use_one_skill));
        Assert.That(types, Has.None.EqualTo(ConditionType.survival_win_without_interact));
        Assert.That(types, Has.None.EqualTo(ConditionType.survival_win_without_npckill));
    }

    // 21300042 "Defeat 10 players in one Mushking Royale match": target 10, at least.
    [TestCase(9, false)]
    [TestCase(10, true)]
    [TestCase(12, true)]
    public void Kill_TargetIsTheMatchKillsAtLeast(long kills, bool expected) {
        var condition = new ConditionMetadata(ConditionType.survival_kill, 1, null, new ConditionMetadata.Parameters(Integers: [10]));
        Assert.That(condition.Check(null!, targetLong: kills), Is.EqualTo(expected));
    }

    // 21300058 "Place 5th in Mushking Royale without defeating anyone": code 5 (the rank, at most), target 0 (the
    // kills, at most).
    [TestCase(5, 0, true)]
    [TestCase(1, 0, true)]
    [TestCase(6, 0, false)]
    [TestCase(5, 1, false)]
    public void RankWithKill_RankAndKillsAtMost(long rank, long kills, bool expected) {
        var condition = new ConditionMetadata(ConditionType.survival_rank_with_kill, 1,
            new ConditionMetadata.Parameters(Integers: [5]), new ConditionMetadata.Parameters(Integers: [0]));
        Assert.That(condition.Check(null!, targetLong: kills, codeLong: rank), Is.EqualTo(expected));
    }

    // 21300062 "Obtain 50 ... in one match": code the item, target 50.
    [TestCase(20000733, 50, true)]
    [TestCase(20000733, 49, false)]
    [TestCase(20000737, 50, false)]
    public void ItemGet_CodeIsTheItemTargetTheAmount(long itemId, long amount, bool expected) {
        var condition = new ConditionMetadata(ConditionType.survival_item_get, 1,
            new ConditionMetadata.Parameters(Integers: [20000733]), new ConditionMetadata.Parameters(Integers: [50]));
        Assert.That(condition.Check(null!, targetLong: amount, codeLong: itemId), Is.EqualTo(expected));
    }

    // 21300057 lists the six levels of one weapon's skill; 21300073 two maps.
    [TestCase(ConditionType.survival_total_kill_use_single_skill, 90000798, true)]
    [TestCase(ConditionType.survival_total_kill_use_single_skill, Jab, false)]
    [TestCase(ConditionType.survive_map, 82000001, true)]
    [TestCase(ConditionType.survive_map, 82000003, false)]
    public void Code_IsOneOfTheListed(ConditionType type, long code, bool expected) {
        int[] codes = type == ConditionType.survive_map
            ? [82000012, 82000001]
            : [90000785, 90000786, 90000787, 90000798, 90000799, 90000800];
        var condition = new ConditionMetadata(type, 1, new ConditionMetadata.Parameters(Integers: codes), null);
        Assert.That(condition.Check(null!, codeLong: code), Is.EqualTo(expected));
    }
}
