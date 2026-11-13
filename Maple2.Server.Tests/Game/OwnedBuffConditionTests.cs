using System.Collections.Concurrent;
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

// hasBuffOwner (HasBuff.Owned): the buff the condition tests must be one its caster cast, as the range select's
// targetHasBuffOwner; without it any copy counts. Surprise Attack 10700091 needs the thief's own 10700011 on its target.
public class OwnedBuffConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int BuffId = 10700011;

    private static FieldPlayer Player(TestGameSession session, int objectId) {
        FieldPlayer player = session.Session.Player;
        typeof(Actor<Maple2.Model.Game.Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(player, objectId);
        return player;
    }

    // A copy of BuffId on the holder, cast by the caster, with only what the condition reads.
    private static void Give(IActor holder, IActor caster, int stacks = 1) {
        var metadata = (AdditionalEffectMetadata) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMetadata));
        typeof(AdditionalEffectMetadata).GetField("<Id>k__BackingField", Any)!.SetValue(metadata, BuffId);
        typeof(AdditionalEffectMetadata).GetField("<Level>k__BackingField", Any)!.SetValue(metadata, (short) 1);
        var buff = (Buff) RuntimeHelpers.GetUninitializedObject(typeof(Buff));
        typeof(Buff).GetField("Metadata", Any)!.SetValue(buff, metadata);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        typeof(Buff).GetField("<Stacks>k__BackingField", Any)!.SetValue(buff, stacks);
        typeof(Buff).GetField("<Enabled>k__BackingField", Any)!.SetValue(buff, true);
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        buffs.GetOrAdd(BuffId, _ => []).Add(buff);
    }

    private static BeginConditionTarget Test(bool owned, int count = 0) {
        return new BeginConditionTarget([new BeginConditionTarget.HasBuff(BuffId, 0, owned, count, CompareType.Equals)],
            new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], []);
    }

    // A condition with only a target test, or only a caster test (battle mounts allowed: a test session has no ride).
    private static BeginCondition Condition(BeginConditionTarget? caster, BeginConditionTarget? target) {
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, target, null, caster);
    }

    [Test]
    public void TargetTest_TakesOnlyTheCastersOwnCopy() {
        var ta = new TestGameSession();
        ta.Start();
        var tb = new TestGameSession(ta.Options);
        tb.Start();
        var tt = new TestGameSession(ta.Options);
        tt.Start();
        FieldPlayer a = Player(ta, 1), b = Player(tb, 2), target = Player(tt, 3);
        Give(target, a);
        BeginCondition owned = Condition(null, Test(owned: true));
        BeginCondition any = Condition(null, Test(owned: false));
        Assert.Multiple(() => {
            Assert.That(owned.Check(a, a, target), Is.True, "A's own mark");
            Assert.That(owned.Check(b, b, target), Is.False, "B's cast on A's mark");
            Assert.That(any.Check(b, b, target), Is.True, "without hasBuffOwner any copy counts");
        });
        Give(target, b);
        Assert.That(owned.Check(b, b, target), Is.True, "B's own copy beside A's");
    }

    // hasNotBuffOwner (HasNotBuffOwned): only the caster's own copy of the hasNotBuffID buff fails the test, so another
    // caster's 10300227 on the target does not stop this caster's 10300222; without the flag any copy does.
    [Test]
    public void NotBuffTest_OnlyTheCastersOwnCopyFails() {
        var ta = new TestGameSession();
        ta.Start();
        var tb = new TestGameSession(ta.Options);
        tb.Start();
        var tt = new TestGameSession(ta.Options);
        tt.Start();
        FieldPlayer a = Player(ta, 1), b = Player(tb, 2), target = Player(tt, 3);
        BeginConditionTarget empty = Test(owned: false) with { Buff = [] };
        BeginCondition owned = Condition(null, empty with { HasNotBuffIds = [BuffId], HasNotBuffOwned = true });
        BeginCondition any = Condition(null, empty with { HasNotBuffIds = [BuffId] });
        Assert.That(owned.Check(a, a, target) && any.Check(a, a, target), Is.True, "no copy on the target");
        Give(target, a);
        Assert.Multiple(() => {
            Assert.That(owned.Check(a, a, target), Is.False, "A's own copy");
            Assert.That(owned.Check(b, b, target), Is.True, "A's copy does not stop B");
            Assert.That(any.Check(b, b, target), Is.False, "without hasNotBuffOwner any copy does");
        });
        // Only an enabled copy fails it, as the client's buff tests count only enabled records.
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(target.Buffs)!;
        typeof(Buff).GetField("<Enabled>k__BackingField", Any)!.SetValue(buffs[BuffId][0], false);
        Assert.That(buffs[BuffId][0].Enabled, Is.False);
        Assert.That(owned.Check(a, a, target) && any.Check(b, b, target), Is.True, "a disabled copy");
    }

    // The stack count is compared on the caster's own copy: another caster's copy with the count does not pass.
    [Test]
    public void TargetTest_CountsTheOwnCopysStacks() {
        var ta = new TestGameSession();
        ta.Start();
        var tb = new TestGameSession(ta.Options);
        tb.Start();
        var tt = new TestGameSession(ta.Options);
        tt.Start();
        FieldPlayer a = Player(ta, 1), b = Player(tb, 2), target = Player(tt, 3);
        Give(target, a, stacks: 3);
        Give(target, b, stacks: 1);
        BeginCondition three = Condition(null, Test(owned: true, count: 3));
        Assert.Multiple(() => {
            Assert.That(three.Check(a, a, target), Is.True);
            Assert.That(three.Check(b, b, target), Is.False);
        });
    }

    // A caster test with hasBuffOwner takes the buff the caster gave itself, not one another player gave it.
    [Test]
    public void CasterTest_TakesTheBuffTheCasterGaveItself() {
        var ta = new TestGameSession();
        ta.Start();
        var tb = new TestGameSession(ta.Options);
        tb.Start();
        FieldPlayer a = Player(ta, 1), b = Player(tb, 2);
        BeginCondition owned = Condition(Test(owned: true), null);
        Give(a, b);
        Assert.That(owned.Check(a, a, b), Is.False, "the copy B gave A");
        Give(a, a);
        Assert.That(owned.Check(a, a, b), Is.True, "A's own copy");
    }

    // A mob that keeps the effects its buffs' event skills apply instead of applying them.
    private sealed class EffectLogNpc : FieldNpc {
        public List<(IActor Caster, IActor Owner, SkillEffectMetadata Effect)> Applied = null!;

        private EffectLogNpc() : base(null!, 0, null!, null!, null!) { }

        public override void ApplyEffect(IActor caster, IActor owner, SkillEffectMetadata effect, long startTick,
                EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, bool notifyField = true) {
            Applied.Add((caster, owner, effect));
        }
    }

    private const int Stacked = 10300271;

    private static EffectLogNpc Mob(FieldManager field, int objectId) {
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
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

    // 10300271 on its holder, cast by the caster: at 10 stacks its event skill casts 10300275 when the holder holds the
    // caster's own copy at 10 (the Owner test with hasBuffOwner, as in the KR data).
    private static Buff Debuff(IActor holder, IActor caster, int stacks) {
        var payoff = new SkillEffectMetadata(0, [new SkillEffectMetadata.Skill(10300275, 1)],
            new SkillEffectMetadataCondition(Condition(null, null) with {
                Owner = new BeginConditionTarget([new BeginConditionTarget.HasBuff(Stacked, 0, true, 10, CompareType.GreaterEquals)],
                    new BeginConditionTarget.EventCondition(EventConditionType.OnBuffStacksReached, false, [], []), [], [], [],
                    new Dictionary<MasteryType, int>(), [], []),
            }, SkillTargetType.Owner, SkillTargetType.Owner, 0, false), null);
        AdditionalEffectMetadata metadata = DisabledBuffTests.Effect(Stacked, property: DisabledBuffTests.Property with { MaxCount = 10 })
            with { Skills = [payoff] };
        Buff buff = DisabledBuffTests.Held(metadata, enabled: true, stacks);
        typeof(Buff).GetField("<Caster>k__BackingField", Any)!.SetValue(buff, caster);
        typeof(Buff).GetField("<Owner>k__BackingField", Any)!.SetValue(buff, holder);
        var buffs = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(holder.Buffs)!;
        buffs.GetOrAdd(Stacked, _ => []).Add(buff);
        return buff;
    }

    // A player's debuff on a mob reaching its stacks fires its hasBuffOwner payoff alone: the stack event runs on the
    // holder, but the test takes the copies of the buff's caster (BuffManager.TriggerEvent).
    [Test]
    public void StacksReached_OnAnEnemy_TakesTheBuffCastersCopy() {
        var ta = new TestGameSession();
        ta.Start();
        FieldManager field = ta.Session.Field!;
        FieldPlayer a = Player(ta, 1);
        EffectLogNpc mob = Mob(field, 10);
        Buff mark = Debuff(mob, a, stacks: 9);
        Assert.That(mark.Stack(), Is.True);
        Assert.That(mob.Applied, Has.Count.EqualTo(1), "A's mark at 10 on the mob");
        Assert.That(mob.Applied[0].Effect.Skills[0].Id, Is.EqualTo(10300275));
    }

    // Two players' copies on one mob: B's reaching 10 fires once, from B's copy; A's copy at 3 does not pass for A.
    [Test]
    public void StacksReached_TwoCasters_OnlyTheFullCopysCaster() {
        var ta = new TestGameSession();
        ta.Start();
        var tb = new TestGameSession(ta.Options);
        tb.Start();
        FieldManager field = ta.Session.Field!;
        FieldPlayer a = Player(ta, 1), b = Player(tb, 2);
        EffectLogNpc mob = Mob(field, 10);
        Debuff(mob, a, stacks: 3);
        Buff full = Debuff(mob, b, stacks: 9);
        Assert.That(full.Stack(), Is.True);
        Assert.That(mob.Applied, Has.Count.EqualTo(1));
    }
}
