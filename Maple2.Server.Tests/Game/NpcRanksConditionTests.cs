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
// list. KR: the proc 90000296 of the potion buff 90000499 has eventCondition 6 (OnOwnerAttackHit) on owner and only
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

    private static BeginConditionTarget Sub(EventConditionType type, int[]? classes = null) {
        return new BeginConditionTarget([], new BeginConditionTarget.EventCondition(type, false, [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], [], NpcClasses: classes);
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
}
