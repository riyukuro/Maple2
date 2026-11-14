using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// The massive event lines (91-104, 161, 162) raise a mini game round's exp and the move speed only in their game's
// field, and reduce_recovery_ep_inv (129) shortens the stamina regen interval.
public class MassiveAbilityTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;

    private GameSession Start(int mapId, long miniGamePrestige = 0) {
        test = new TestGameSession();
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> {
            [ExpType.miniGame] = new(1, 1f),
            [ExpType.userMiniGame] = new(1, 1f),
            [ExpType.fishing] = new(1, 1f),
        }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 1000L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000_000L)));
        test.SetServerTable(new PrestigeExpTable(miniGamePrestige > 0
            ? new Dictionary<ExpType, long> { [ExpType.miniGame] = miniGamePrestige }
            : new Dictionary<ExpType, long>()));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(mapId));
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<Field>k__BackingField", Any)!.SetValue(session.Player, session.Field);
        session.Player.Value.Character.Level = 50;
        return session;
    }

    [TestCase(61000003, SpecialAttribute.MassiveOxExp, SpecialAttribute.MassiveOxSpeed)]
    [TestCase(61000011, SpecialAttribute.MassiveOxExp, SpecialAttribute.MassiveOxSpeed)]
    [TestCase(61000021, SpecialAttribute.MassiveOxExp, SpecialAttribute.MassiveOxSpeed)]
    [TestCase(61000001, SpecialAttribute.MassiveTrapMasterExp, SpecialAttribute.MassiveTrapMasterSpeed)]
    [TestCase(61000002, SpecialAttribute.MassiveFinalSurvivalExp, SpecialAttribute.MassiveFinalSurvivalSpeed)]
    [TestCase(61000006, SpecialAttribute.MassiveCrazyRunnerExp, SpecialAttribute.MassiveCrazyRunnerSpeed)]
    [TestCase(81000003, SpecialAttribute.MassiveCrazyRunnerExp, SpecialAttribute.MassiveCrazyRunnerSpeed)]
    [TestCase(61000004, SpecialAttribute.MassiveEscapeExp, SpecialAttribute.MassiveEscapeSpeed)]
    [TestCase(81000001, SpecialAttribute.MassiveEscapeExp, SpecialAttribute.MassiveEscapeSpeed)]
    [TestCase(61000007, SpecialAttribute.MassiveSpringBeachExp, SpecialAttribute.MassiveSpringBeachSpeed)]
    [TestCase(81000002, SpecialAttribute.MassiveSpringBeachExp, SpecialAttribute.MassiveSpringBeachSpeed)]
    [TestCase(61000008, SpecialAttribute.MassiveDanceDanceExp, SpecialAttribute.MassiveDanceDanceSpeed)]
    [TestCase(61000022, SpecialAttribute.MassiveDanceDanceExp, SpecialAttribute.MassiveDanceDanceSpeed)]
    [TestCase(61000010, SpecialAttribute.MassiveShCrazyRunnerExp, SpecialAttribute.MassiveShCrazyRunnerSpeed)]
    public void LinesOf(int mapId, SpecialAttribute exp, SpecialAttribute speed) {
        Assert.That(MassiveAbilityRules.LinesOf(mapId), Is.EqualTo((exp, speed)));
        Assert.That(MassiveAbilityRules.ExpAttribute(ExpType.miniGame, mapId), Is.EqualTo(exp));
        Assert.That(MassiveAbilityRules.ExpAttribute(ExpType.userMiniGame, mapId), Is.EqualTo(exp));
        Assert.That(MassiveAbilityRules.ExpAttribute(ExpType.fishing, mapId), Is.Null);
        Assert.That(MassiveAbilityRules.SpeedAttribute(mapId), Is.EqualTo(speed));
    }

    // The wedding hall's dance game and plain fields have no line.
    [TestCase(84000007)]
    [TestCase(2000023)]
    [TestCase(0)]
    public void NoLines(int mapId) {
        Assert.That(MassiveAbilityRules.LinesOf(mapId), Is.Null);
        Assert.That(MassiveAbilityRules.ExpAttribute(ExpType.miniGame, mapId), Is.Null);
        Assert.That(MassiveAbilityRules.SpeedAttribute(mapId), Is.Null);
    }

    [TestCase(2000023, 61000003, true)]
    [TestCase(61000003, 2000023, true)]
    [TestCase(61000003, 61000001, true)]
    [TestCase(61000003, 61000011, false)]
    [TestCase(61000006, 81000003, false)]
    [TestCase(2000023, 84000007, false)]
    [TestCase(null, 61000003, true)]
    [TestCase(null, 2000023, false)]
    public void RebuildsOnEntry(int? left, int? entered, bool rebuilds) {
        Assert.That(MassiveAbilityRules.RebuildsOnEntry(left, entered), Is.EqualTo(rebuilds));
    }

    [TestCase(61000003, 5)]
    [TestCase(61000001, 0)]
    [TestCase(2000023, 0)]
    public void SpeedLineAddsOnlyInItsField(int mapId, long added) {
        GameSession session = Start(mapId);
        Stats stats = session.Player.Stats.Values;
        stats[SpecialAttribute.MassiveOxSpeed].AddTotal(5);
        long speed = stats[BasicAttribute.MovementSpeed].Total;

        session.Player.Stats.AddMassiveAbilities(session.Player);

        Assert.That(stats[BasicAttribute.MovementSpeed].Total - speed, Is.EqualTo(added));
    }

    // 150 (15 %) on the OX line raises an OX round's exp by 15 % in the OX field only, and no other exp type.
    [TestCase(61000003, ExpType.miniGame, 1.15)]
    [TestCase(61000003, ExpType.userMiniGame, 1.15)]
    [TestCase(61000003, ExpType.fishing, 1.0)]
    [TestCase(61000001, ExpType.miniGame, 1.0)]
    [TestCase(2000023, ExpType.miniGame, 1.0)]
    public void ExpLineRaisesOnlyItsGamesRounds(int mapId, ExpType type, double ratio) {
        GameSession session = Start(mapId);
        long plain = session.Exp.AddExp(type);
        Assert.That(plain, Is.GreaterThan(0));
        session.Player.Stats.Values[SpecialAttribute.MassiveOxExp].AddTotal(150);

        long lined = session.Exp.AddExp(type);

        Assert.That((double) lined / plain, Is.EqualTo(ratio).Within(0.001));
    }

    // The OX line raises the round's exp but not its prestige exp: two players, one with the line, gain the same.
    [Test]
    public void ExpLineLeavesPrestigeExp() {
        GameSession plain = Start(61000003, miniGamePrestige: 1000);
        plain.Exp.AddExp(ExpType.miniGame);
        long plainPrestige = plain.Exp.PrestigeCurrentExp;
        Assert.That(plainPrestige, Is.GreaterThan(0));

        GameSession lined = Start(61000003, miniGamePrestige: 1000);
        lined.Player.Stats.Values[SpecialAttribute.MassiveOxExp].AddTotal(150);
        lined.Exp.AddExp(ExpType.miniGame);

        Assert.That(lined.Exp.PrestigeCurrentExp, Is.EqualTo(plainPrestige));
    }

    // A field entry that keeps the speed line sends the player's stats itself, once (a rebuild sends them in Refresh).
    [TestCase(61000003, 61000011)]
    [TestCase(2000023, 2000023)]
    public void EntryWithoutRebuildSendsTheStatsOnce(int left, int entered) {
        GameSession session = Start(entered);
        test.TakeSent();

        session.SendEntryStats(null, left);

        Assert.That(test.TakeSentOps().Count(op => op == SendOp.Stat), Is.EqualTo(1));
    }

    // On a battle ride the entry's rebuild is left for the ride's end (Refresh returns before the rebuild), so the
    // entry sends the stats itself, once.
    [Test]
    public void BattleRideDefersTheEntryRebuild() {
        GameSession session = Start(61000003);
        typeof(StatsManager).GetProperty(nameof(StatsManager.Actor))!.SetValue(session.Stats, session.Player);
        session.Ride = new RideManager(session);
        var metadata = new RideMetadata(99910380, "", new RideMetadataBasic(RideOnType.Battle, 0, 0, 0, false, 0, 0),
            new RideMetadataSpeed(1, 1, 1, 1), new Dictionary<BasicAttribute, long>());
        session.Ride.Ride = new Ride(session.Player.ObjectId, metadata, new RideOnActionBattle(metadata.Id, 1, 0, 1));
        test.TakeSent();

        session.SendEntryStats(null, 2000023);

        Assert.That(session.Player.Stats.RefreshDeferred, Is.True);
        Assert.That(test.TakeSentOps().Count(op => op == SendOp.Stat), Is.EqualTo(1));
    }

    // The ride's end rebuilds a deferred refresh even for a ride without stats of its own.
    [TestCase(0, true, true)]
    [TestCase(0, false, false)]
    [TestCase(3, false, true)]
    public void RideEndRebuilds(int rideStats, bool deferred, bool rebuilds) {
        Assert.That(RideShare.RebuildsAtEnd(rideStats, deferred), Is.EqualTo(rebuilds));
    }

    [TestCase(0, 0f)]
    [TestCase(100, 0.1f)]
    [TestCase(220, 0.22f)]
    [TestCase(950, 0.9f)]
    [TestCase(-50, 0f)]
    public void IntervalCut(long rate, float cut) {
        Assert.That(StaminaRecoveryRules.IntervalCut(rate), Is.EqualTo(cut).Within(1e-6));
    }

    // 100 (10 %) makes the 500 ms regen interval 450 ms.
    [Test]
    public void StaminaLineShortensTheRegenInterval() {
        GameSession session = Start(2000023);
        Stats stats = session.Player.Stats.Values;
        long interval = stats[BasicAttribute.StaminaRegenInterval].Total;
        stats[SpecialAttribute.ReduceRecoveryEpInv].AddTotal(100);

        session.Player.Stats.AddStaminaRecovery();
        stats.Total();

        Assert.That(stats[BasicAttribute.StaminaRegenInterval].Total, Is.EqualTo(interval * 9 / 10));
    }
}
