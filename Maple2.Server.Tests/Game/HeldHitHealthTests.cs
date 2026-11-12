using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The health a player's hits send to the world (the party frames of members in other fields), through
// FieldPlayer.ApplyDamage, Update and the leave of a field.
public class HeldHitHealthTests {
    private TestGameSession test = null!;
    private FieldPlayer player => test.Session.Player;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        // The fixture's actor is not constructed: what a hit and the health clock read is set here.
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(player, test.Session.Field);
        typeof(Actor<Player>).GetField("DamageDealers", any)!.SetValue(player, new ConcurrentDictionary<int, DamageRecordTarget>());
        typeof(FieldPlayer).GetField("healthSync", any)!.SetValue(player, new HealthSyncClock());
        typeof(FieldPlayer).GetField("regenClock", any)!.SetValue(player, new RegenClock());
        Stat health = player.Stats.Values[BasicAttribute.Health];
        health.AddTotal(1000 - health.Total);
        health.Add(1000 - health.Current);
        test.World.PlayerUpdates.Clear();
    }

    // A hit of a fixed amount, from the player's own actor (the caster does not matter for the health sent).
    private void Hit(long amount) {
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(1, 1, null, property, null!, null!);
        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 0, HitSpeed: 0, HitDelay: 0, IsConstDamage: true, Value: amount,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false, Push: null),
        };
        player.ApplyDamage(player, new DamageRecord(skill, attack), attack);
    }

    private long[] SentHealth() {
        long[] sent = test.World.PlayerUpdates.Where(update => update.Health != null).Select(update => update.Health.CurrentHp).ToArray();
        test.World.PlayerUpdates.Clear();
        return sent;
    }

    [Test]
    public void HitsInsideOneIntervalSendTheFirstAndThenTheLatestHealth() {
        player.UpdateHealthSync(10_000);
        Hit(100);
        Hit(100);
        Hit(100);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 900 }));

        player.UpdateHealthSync(10_000 + HealthSyncClock.IntervalTick - 1);
        Hit(100);
        Assert.That(SentHealth(), Is.Empty);

        player.UpdateHealthSync(10_000 + HealthSyncClock.IntervalTick);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 600 }));

        player.UpdateHealthSync(10_000 + 2 * HealthSyncClock.IntervalTick);
        Assert.That(SentHealth(), Is.Empty);
    }

    [Test]
    public void TheKillingHitGoesAtOnce() {
        player.UpdateHealthSync(10_000);
        Hit(100);
        Hit(100);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 900 }));

        Hit(5000);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 0 }));

        player.UpdateHealthSync(10_000 + HealthSyncClock.IntervalTick);
        Assert.That(SentHealth(), Is.Empty);
    }

    // FieldManager.RemovePlayer itself: the held health goes last, after the leave is broadcast.
    [Test]
    public void RemovePlayerSendsTheHeldHealth() {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldManager field = test.Session.Field!;
        // What the leave reads that the fixture's field does not set.
        typeof(FieldManager).GetField("<GameStorage>k__BackingField", any)!.SetValue(field, test.Storage);
        typeof(FieldManager).GetField("fieldReactors", any)!.SetValue(field, new ConcurrentDictionary<int, FieldReactor>());
        typeof(FieldManager).GetField("<Rps>k__BackingField", any)!.SetValue(field, new RpsMatchTable());
        typeof(FieldManager).GetField("<CoupleDance>k__BackingField", any)!.SetValue(field, new CoupleDanceMatchTable());
        test.Session.Ride = new RideManager(test.Session);
        player.UpdateHealthSync(10_000);
        Hit(100);
        Hit(100);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 900 }));

        Assert.That(field.RemovePlayer(player.ObjectId, out FieldPlayer? removed), Is.True);

        Assert.That(removed, Is.SameAs(player));
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 800 }));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void AHeldHealthGoesWhenThePlayerLeavesTheField() {
        player.UpdateHealthSync(10_000);
        Hit(100);
        Hit(100);
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 900 }));

        player.FlushHealth();
        Assert.That(SentHealth(), Is.EqualTo(new long[] { 800 }));

        player.FlushHealth();
        Assert.That(SentHealth(), Is.Empty);
    }

    [Test]
    public void ALeaveWithNothingHeldSendsNoHealth() {
        player.UpdateHealthSync(10_000);
        Hit(100);
        SentHealth();

        player.FlushHealth();
        Assert.That(SentHealth(), Is.Empty);
    }
}
