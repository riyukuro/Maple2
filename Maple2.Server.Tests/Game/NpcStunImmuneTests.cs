using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A held stun buff takes a monster only when its abnormalImmuneBreak is above the npc's abnormalImmune (equal does
// not break it); the buff stays held either way. KR data: 6,043 npcs have abnormalImmune 1, 1,477 have 99, and the
// player skills' stuns carry break 5, so a 99 boss is no longer stunned by them. Before, every stun took every npc.
public class NpcStunImmuneTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // An npc with only what the stun gate reads: its abnormalImmune, its buffs and a logger.
    private static FieldNpc Npc(int abnormalImmune) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<AbnormalImmune>k__BackingField", Any)!.SetValue(basic, abnormalImmune);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(npc, new BuffManager(npc));
        typeof(Actor<Npc>).GetField("Logger", Any)!.SetValue(npc, Serilog.Log.Logger);
        return npc;
    }

    private static ConcurrentDictionary<int, List<Buff>> Held(FieldNpc npc) {
        return (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(npc.Buffs)!;
    }

    private static void Give(FieldNpc npc, int id, int stun, int immuneBreak) {
        AdditionalEffectMetadata effect = DisabledBuffTests.Effect(id, property: DisabledBuffTests.Property with { Stun = stun, ImmuneBreak = immuneBreak });
        Held(npc)[id] = [DisabledBuffTests.Held(effect, enabled: true)];
    }

    [Test]
    public void BreakAboveImmuneOnly() {
        Assert.That(NpcControlRules.StunTakes(5, 1), Is.True);
        Assert.That(NpcControlRules.StunTakes(1, 0), Is.True);
        Assert.That(NpcControlRules.StunTakes(99, 98), Is.True);
        Assert.That(NpcControlRules.StunTakes(99, 99), Is.False);
        Assert.That(NpcControlRules.StunTakes(5, 5), Is.False);
        Assert.That(NpcControlRules.StunTakes(5, 99), Is.False);
        Assert.That(NpcControlRules.StunTakes(-1, 0), Is.False);
    }

    [Test]
    public void StunBreakIsTheHighestOfTheStunBuffs() {
        AdditionalEffectMetadata Stun(int id, int stun, int immuneBreak) =>
            DisabledBuffTests.Effect(id, property: DisabledBuffTests.Property with { Stun = stun, ImmuneBreak = immuneBreak });
        Assert.That(new BuffSums([]).StunBreak, Is.EqualTo(-1));
        Assert.That(new BuffSums([(Stun(1, 2, 99), 1)]).StunBreak, Is.EqualTo(-1));
        Assert.That(new BuffSums([(Stun(1, 1, 5), 1)]).StunBreak, Is.EqualTo(5));
        Assert.That(new BuffSums([(Stun(1, 1, 5), 1), (Stun(2, 3, 99), 1)]).StunBreak, Is.EqualTo(99));
        // A non-stun value's break does not count.
        Assert.That(new BuffSums([(Stun(1, 1, 5), 1), (Stun(2, 2, 99), 1)]).StunBreak, Is.EqualTo(5));
    }

    [Test]
    public void PlayerStunTakesAMobButNotABoss() {
        FieldNpc mob = Npc(1);
        Give(mob, 10, 1, 5);
        Assert.That(mob.TakesStun(), Is.True);

        FieldNpc boss = Npc(99);
        Give(boss, 10, 1, 5);
        Assert.That(boss.TakesStun(), Is.False);
        Assert.That(boss.TakesStun(), Is.False);
        // The buff stays held.
        Assert.That(Held(boss).ContainsKey(10), Is.True);
        Assert.That(boss.Buffs.Stunned, Is.True);
    }

    [Test]
    public void Break99TakesImmune98NotImmune99() {
        FieldNpc a = Npc(98);
        Give(a, 10, 3, 99);
        Assert.That(a.TakesStun(), Is.True);
        FieldNpc b = Npc(99);
        Give(b, 10, 3, 99);
        Assert.That(b.TakesStun(), Is.False);
    }

    [Test]
    public void OneStunBuffThatBreaksIsEnough() {
        FieldNpc boss = Npc(90);
        Give(boss, 10, 1, 5);
        Give(boss, 11, 1, 95);
        Assert.That(boss.TakesStun(), Is.True);
    }

    [Test]
    public void NoStunBuffNoStun() {
        FieldNpc mob = Npc(0);
        Give(mob, 10, 2, 99);
        Assert.That(mob.TakesStun(), Is.False);
    }
}
