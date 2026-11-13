using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// A target condition's NpcRanks (BeginConditionTarget.NpcClasses): it holds only for an npc whose class is in the
// list. Also the event an event-less sub-condition takes from its sibling, and eventIgnoreSkillID. KR: the proc 90000296 of the potion buff 90000499 has eventCondition 6 (OnOwnerAttackHit) on owner and only
// NpcRanks 7 on target; the target sub-condition names no event and holds for the owner's one.
public class NpcRanksConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Proc = 90000296;

    // A mob of the given class that keeps the effects its buffs' event skills apply instead of applying them.
    private sealed class EffectLogNpc : FieldNpc {
        public List<(IActor Caster, IActor Owner, SkillEffectMetadata Effect)> Applied = null!;

        private EffectLogNpc() : base(null!, 0, null!, null!, null!) { }

        public override void ApplyEffect(IActor caster, IActor owner, SkillEffectMetadata effect, long startTick,
                EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, bool notifyField = true) {
            Applied.Add((caster, owner, effect));
        }
    }

    private static EffectLogNpc Npc(FieldManager? field, int objectId, int @class) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<Class>k__BackingField", Any)!.SetValue(basic, @class);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        var npc = (EffectLogNpc) RuntimeHelpers.GetUninitializedObject(typeof(EffectLogNpc));
        npc.Applied = [];
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        typeof(Actor<Npc>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(npc, objectId);
        typeof(Actor<Npc>).GetField("<Transform>k__BackingField", Any)!.SetValue(npc, new Transform());
        typeof(Actor<Npc>).GetField("<Field>k__BackingField", Any)!.SetValue(npc, field);
        typeof(Actor<Npc>).GetField("<Buffs>k__BackingField", Any)!.SetValue(npc, new BuffManager(npc));
        npc.Position = Vector3.Zero;
        return npc;
    }

    private static BeginConditionTarget Sub(EventConditionType type, int[]? classes = null, BeginConditionTarget.HasBuff[]? buffs = null,
            int[]? skillIds = null, int[]? ignoreSkillIds = null) {
        return new BeginConditionTarget(buffs ?? [], new BeginConditionTarget.EventCondition(type, false, skillIds ?? [], [], ignoreSkillIds ?? []),
            [], [], [], new Dictionary<MasteryType, int>(), [], [], NpcClasses: classes);
    }

    private static BeginCondition Of(BeginConditionTarget? caster, BeginConditionTarget? owner, BeginConditionTarget? target) {
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, target, owner, caster);
    }

    // Puts an enabled level-1 buff with the given id, cast by caster, on holder.
    private static void Give(IActor holder, int id, IActor caster) {
        Buff buff = DisabledBuffTests.Held(DisabledBuffTests.Effect(id), enabled: true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        var buffs = (System.Collections.Concurrent.ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        buffs.GetOrAdd(id, _ => []).Add(buff);
    }

    // As in the KR data: owner eventCondition 6, target NpcRanks only.
    private static BeginCondition Condition(int[]? classes) {
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null,
            classes is null ? null : Sub(EventConditionType.Activate, classes), Sub(EventConditionType.OnOwnerAttackHit), null);
    }

    [Test]
    public void HoldsOnlyForAListedClass() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        Assert.Multiple(() => {
            Assert.That(Condition([7]).Check(player, player, Npc(null, 20, 7), EventConditionType.OnOwnerAttackHit), Is.True);
            Assert.That(Condition([7]).Check(player, player, Npc(null, 21, 3), EventConditionType.OnOwnerAttackHit), Is.False);
            Assert.That(Condition([7]).Check(player, player, player, EventConditionType.OnOwnerAttackHit), Is.False, "not an npc");
            Assert.That(Condition(null).Check(player, player, Npc(null, 22, 3), EventConditionType.OnOwnerAttackHit), Is.True, "no list");
            Assert.That(Condition([7]).Check(player, player, Npc(null, 23, 7), EventConditionType.OnOwnerAttackCrit), Is.False, "other event");
            Assert.That(Condition([7]).Check(player, player, Npc(null, 24, 7)), Is.False, "activate");
        });
    }

    // With no sibling naming an event, an event-less sub-condition still needs event 0, as before.
    [Test]
    public void NoNamedEvent_StaysActivateOnly() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        BeginCondition plain = Condition(null) with { Owner = null, Target = Sub(EventConditionType.Activate, [7]) };
        Assert.That(plain.Check(player, player, Npc(null, 25, 7)), Is.True);
        Assert.That(plain.Check(player, player, Npc(null, 26, 7), EventConditionType.OnOwnerAttackHit), Is.False);
    }

    // A hit on a class-7 npc fires the proc through the holder's buff (Actor damage -> BuffManager.TriggerEvent); a
    // class-3 npc does not.
    [Test]
    public void AttackHit_OnAClass7Npc_FiresTheProc() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        EffectLogNpc holder = Npc(field, 10, 1), chaw = Npc(field, 11, 7), other = Npc(field, 12, 3);
        var proc = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(Proc, 1)],
            new SkillEffectMetadataCondition(Condition([7]), SkillTargetType.None, SkillTargetType.Target, 0, false), null);
        Buff potion = DisabledBuffTests.Held(DisabledBuffTests.Effect(90000499) with { Skills = [proc] }, enabled: true);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(potion, holder);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(potion, holder);
        var buffs = (System.Collections.Concurrent.ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        buffs.GetOrAdd(90000499, _ => []).Add(potion);

        holder.Buffs.TriggerEvent(holder, holder, other, EventConditionType.OnOwnerAttackHit, skillId: 1);
        Assert.That(holder.Applied, Is.Empty, "class 3");
        holder.Buffs.TriggerEvent(holder, holder, chaw, EventConditionType.OnOwnerAttackHit, skillId: 1);
        Assert.That(holder.Applied, Has.Count.EqualTo(1), "class 7");
        Assert.That(holder.Applied[0].Effect.Skills[0].Id, Is.EqualTo(Proc));
        Assert.That(holder.Applied[0].Owner, Is.SameAs(chaw), "skillTarget 2: on the npc hit");
    }

    // 10600283 (KR): owner eventCondition 6 with eventSkillID 10600231/2/3, target hasBuffID 10600281 cast by the owner
    // (hasBuffOwner 1) and no event. A 10600231 hit on a target marked by the attacker fires 10600284.
    [Test]
    public void MarkedTarget_FiresOnASkillHit() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        BeginCondition condition = Of(null, Sub(EventConditionType.OnOwnerAttackHit, skillIds: [10600231, 10600232, 10600233]),
            Sub(EventConditionType.Activate, buffs: [new BeginConditionTarget.HasBuff(10600281, 1, true, 0, CompareType.Equals)]));
        EffectLogNpc marked = Npc(null, 30, 3), bare = Npc(null, 31, 3), other = Npc(null, 32, 3);
        Give(marked, 10600281, player);
        Give(other, 10600281, other);
        Assert.Multiple(() => {
            Assert.That(condition.Check(player, player, marked, EventConditionType.OnOwnerAttackHit, 10600231), Is.True);
            Assert.That(condition.Check(player, player, marked, EventConditionType.OnOwnerAttackHit, 1), Is.False, "other skill");
            Assert.That(condition.Check(player, player, bare, EventConditionType.OnOwnerAttackHit, 10600231), Is.False, "no mark");
            Assert.That(condition.Check(player, player, other, EventConditionType.OnOwnerAttackHit, 10600231), Is.False, "mark of another");
            Assert.That(condition.Check(player, player, marked), Is.False, "activate");
        });
    }

    // 50005300 / 50005301 (KR): owner eventCondition 6 with eventIgnoreSkillID 10400062; hits of that skill do not count.
    [Test]
    public void IgnoredSkill_DoesNotFire() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        BeginCondition condition = Of(null, Sub(EventConditionType.OnOwnerAttackHit, ignoreSkillIds: [10400062]), null);
        EffectLogNpc npc = Npc(null, 33, 3);
        Assert.That(condition.Check(player, player, npc, EventConditionType.OnOwnerAttackHit, 10400062), Is.False);
        Assert.That(condition.Check(player, player, npc, EventConditionType.OnOwnerAttackHit, 10400061), Is.True);
    }

    // 10300286 (KR): caster hasBuffID 10300288 and no event, owner eventCondition 16; the caster test borrows event 16
    // and still checks the caster's own buffs.
    [Test]
    public void CasterSub_TakesTheOwnersEvent() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        const EventConditionType named = EventConditionType.OnEffectApplied;
        BeginCondition condition = Of(Sub(EventConditionType.Activate, buffs: [new BeginConditionTarget.HasBuff(10300288, 1, false, 0, CompareType.Equals)]),
            Sub(named), null);
        EffectLogNpc buffed = Npc(null, 34, 3), bare = Npc(null, 35, 3);
        Give(buffed, 10300288, buffed);
        Assert.That(condition.Check(buffed, player, player, named), Is.True);
        Assert.That(condition.Check(bare, player, player, named), Is.False, "no caster buff");
        Assert.That(condition.Check(buffed, player, player), Is.False, "activate");
    }
}
