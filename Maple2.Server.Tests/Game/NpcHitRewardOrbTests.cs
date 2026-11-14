using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game;

// The spirit and stamina orbs of the NpcHitRewardSpBall and NpcHitRewardEpBall rates: the roll and cooldown of
// NpcHitRewardRules, and a hit on a spawned npc (RegionHitApplyTests' field) that drops them for the hitter.
public class NpcHitRewardOrbTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MobId = 21000001;

    [TestCase(0L, 10_000L, 0.0, 0.0, false)]
    [TestCase(0L, 10_000L, 0.5, 0.49, true)]
    [TestCase(0L, 10_000L, 0.5, 0.5, false)]
    [TestCase(10_000L, 12_999L, 1.0, 0.0, false)]
    [TestCase(10_000L, 13_000L, 1.0, 0.0, true)]
    [TestCase(0L, 100L, 1.0, 0.99, true)]
    public void Drops(long lastDropTick, long now, double rate, double roll, bool drops) {
        Assert.That(NpcHitRewardRules.Drops(lastDropTick, now, rate, roll), Is.EqualTo(drops));
    }

    private static ICollection<FieldItem> Ground(FieldManager field) =>
        ((ConcurrentDictionary<int, FieldItem>) typeof(FieldManager).GetField("fieldItems", Any)!.GetValue(field)!).Values;

    private FieldNpc SpawnMob() {
        Start();
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(test.Session.Player, Field);
        // The fixture's actor is not constructed: its orb ticks are set here.
        typeof(FieldPlayer).GetField(nameof(FieldPlayer.HitRewardOrbTicks))!.SetValue(test.Session.Player, new Dictionary<int, long>());
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084),
            TestGameSession.Metadata(NpcHitRewardRules.SpiritOrbItemId), TestGameSession.Metadata(NpcHitRewardRules.StaminaOrbItemId));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field,
            TriggerNpcSpawnTests.NpcTable(TriggerNpcSpawnTests.Npc(MobId, 0)));
        Assert.That(Field.NpcMetadata.TryGet(MobId, out NpcMetadata? metadata), Is.True);
        return Field.SpawnNpc(metadata!, Vector3.Zero, Vector3.Zero)!;
    }

    [Test]
    public void FullSpRate_OneOrbOfTwentyPerCooldown_ForTheHitter() {
        FieldNpc npc = SpawnMob();
        FieldPlayer player = test.Session.Player;
        player.Stats.Values[SpecialAttribute.NpcHitRewardSpBall].AddTotal(1000);

        npc.RecordDamage(player, 10);
        npc.RecordDamage(player, 10);

        List<FieldItem> ground = Ground(Field).ToList();
        Assert.That(ground.Select(item => item.Value.Id), Is.EqualTo(new[] { NpcHitRewardRules.SpiritOrbItemId }));
        Assert.Multiple(() => {
            Assert.That(ground[0].Value.Amount, Is.EqualTo(NpcHitRewardRules.OrbAmount));
            Assert.That(ground[0].ReceiverId, Is.EqualTo(player.Value.Character.Id));
        });

        // Once the cooldown has passed, the next hit drops another.
        player.HitRewardOrbTicks[NpcHitRewardRules.SpiritOrbItemId] -= NpcHitRewardRules.CooldownMs;
        npc.RecordDamage(player, 10);
        Assert.That(Ground(Field).Count, Is.EqualTo(2));
    }

    [Test]
    public void NoRate_NoOrb() {
        FieldNpc npc = SpawnMob();
        npc.RecordDamage(test.Session.Player, 10);
        Assert.That(Ground(Field), Is.Empty);
    }
}
