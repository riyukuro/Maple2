using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// compareStat of an owner condition (KR: 188 effect rows): each listed stat must pass its compare, as a share of the
// stat's total (type 0) or as the total itself (type 1). The rows below are KR Xml rows as ingested.
public class StatConditionTests {
    private static BeginCondition Owner(params BeginConditionTarget.BeginConditionStat[] stats) {
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []),
            stats, [], [], new Dictionary<MasteryType, int>(), [], []);
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, owner, null);
    }

    private static BeginConditionTarget.BeginConditionStat Hp(float value, CompareType compare) =>
        new(BasicAttribute.Health, value, compare, CompareStatValueType.CurrentPercentage);

    private static FieldPlayer Player(long hpCurrent, long hpTotal) {
        var session = new TestGameSession();
        session.Start();
        FieldPlayer player = session.Session.Player;
        Stat hp = player.Stats.Values[BasicAttribute.Health];
        hp.Total = hpTotal;
        hp.Current = hpCurrent;
        return player;
    }

    private static bool Holds(BeginCondition condition, FieldPlayer player) => condition.Check(player, player, player);

    // 10100231: HP at 70% or more (GreaterEquals 0.7).
    [Test]
    public void HpAtLeast70Percent_10100231() {
        BeginCondition condition = Owner(Hp(0.7f, CompareType.GreaterEquals));
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(800, 1000)), Is.True, "80%");
            Assert.That(Holds(condition, Player(700, 1000)), Is.True, "70% exactly");
            Assert.That(Holds(condition, Player(690, 1000)), Is.False, "69%");
        });
    }

    // 10100232: HP at 30% or less (LessEquals 0.3).
    [Test]
    public void HpAtMost30Percent_10100232() {
        BeginCondition condition = Owner(Hp(0.3f, CompareType.LessEquals));
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(200, 1000)), Is.True, "20%");
            Assert.That(Holds(condition, Player(300, 1000)), Is.True, "30% exactly");
            Assert.That(Holds(condition, Player(310, 1000)), Is.False, "31%");
        });
    }

    // 10200081: HP from 45% up to, not including, 60%; both entries must hold.
    [Test]
    public void HpFrom45To60Percent_10200081() {
        BeginCondition condition = Owner(Hp(0.45f, CompareType.GreaterEquals), Hp(0.6f, CompareType.Less));
        Assert.Multiple(() => {
            Assert.That(Holds(condition, Player(500, 1000)), Is.True, "50%");
            Assert.That(Holds(condition, Player(450, 1000)), Is.True, "45% exactly");
            Assert.That(Holds(condition, Player(440, 1000)), Is.False, "44%");
            Assert.That(Holds(condition, Player(600, 1000)), Is.False, "60% exactly");
        });
    }

    // 10700291: attack speed total of 116 or more (type 1, the total itself, not a share).
    [Test]
    public void AttackSpeedTotalAtLeast116_10700291() {
        BeginCondition condition = Owner(new BeginConditionTarget.BeginConditionStat(BasicAttribute.AttackSpeed, 116,
            CompareType.GreaterEquals, CompareStatValueType.TotalValue));
        FieldPlayer player = Player(1000, 1000);
        Stat speed = player.Stats.Values[BasicAttribute.AttackSpeed];
        Assert.Multiple(() => {
            speed.Total = 120;
            speed.Current = 0;
            Assert.That(Holds(condition, player), Is.True, "120, current 0");
            speed.Total = 116;
            Assert.That(Holds(condition, player), Is.True, "116 exactly");
            speed.Total = 115;
            speed.Current = 115;
            Assert.That(Holds(condition, player), Is.False, "115");
        });
    }

    // A stat whose total is 0 counts as a share of 0, so a LessEquals compare holds and a GreaterEquals one fails.
    [Test]
    public void ZeroTotal_IsAShareOfZero() {
        Assert.Multiple(() => {
            Assert.That(Holds(Owner(Hp(0.3f, CompareType.LessEquals)), Player(0, 0)), Is.True);
            Assert.That(Holds(Owner(Hp(0.7f, CompareType.GreaterEquals)), Player(0, 0)), Is.False);
        });
    }

    // 90000626: an owner playing a score (state PlayInstrument 47, sub-state solo 127 or ensemble 129) with Music
    // mastery of 10800 or more; each of the three must hold.
    [Test]
    public void PlayingAScoreWithMusicMastery_90000626() {
        var owner = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []),
            [], [ActorState.PlayInstrument], [ActorSubState.PlayInstrument_Playing_Score_Solo, ActorSubState.PlayInstrument_Playing_Score_Ensemble],
            new Dictionary<MasteryType, int> { [MasteryType.Music] = 10800 }, [], []);
        var condition = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, owner, null);
        FieldPlayer player = Player(1000, 1000);
        player.State = ActorState.PlayInstrument;
        player.SubState = ActorSubState.PlayInstrument_Playing_Score_Ensemble;
        player.Value.Character.Mastery.Instrument = 10800;
        Assert.That(Holds(condition, player), Is.True, "ensemble at 10800");
        player.SubState = ActorSubState.PlayInstrument_Playing_Score_Solo;
        Assert.That(Holds(condition, player), Is.True, "solo");
        player.Value.Character.Mastery.Instrument = 10799;
        Assert.That(Holds(condition, player), Is.False, "mastery 10799");
        player.Value.Character.Mastery.Instrument = 10800;
        player.SubState = ActorSubState.None;
        Assert.That(Holds(condition, player), Is.False, "no score sub-state");
        player.SubState = ActorSubState.PlayInstrument_Playing_Score_Solo;
        player.State = ActorState.Idle;
        Assert.That(Holds(condition, player), Is.False, "not playing");
    }

    // The caster and target tests read their own actor's stats: 40500028 lvl1 (caster HP at 36% or more) and 90000539
    // (target HP over 70%).
    [Test]
    public void CasterAndTargetStats_ReadTheirOwnActor_40500028_90000539() {
        var empty = new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], [], []);
        var casterTest = new BeginConditionTarget([], empty, [Hp(0.36f, CompareType.GreaterEquals)], [], [], new Dictionary<MasteryType, int>(), [], []);
        var targetTest = new BeginConditionTarget([], empty, [Hp(0.7f, CompareType.Greater)], [], [], new Dictionary<MasteryType, int>(), [], []);
        var caster = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, null, null, casterTest);
        var target = new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, targetTest, null, null);
        FieldPlayer high = Player(900, 1000), low = Player(300, 1000);
        Assert.Multiple(() => {
            Assert.That(caster.Check(high, low, low), Is.True, "caster at 90%");
            Assert.That(caster.Check(low, high, high), Is.False, "caster at 30%");
            Assert.That(target.Check(low, low, high), Is.True, "target at 90%");
            Assert.That(target.Check(high, high, low), Is.False, "target at 30%");
            Assert.That(target.Check(high, high, Player(700, 1000)), Is.False, "target at 70% exactly");
        });
    }
}
