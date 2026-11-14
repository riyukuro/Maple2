using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// StateSkill 0 (StateSkill::UseStateSkill, stateskill.cpp:1999): the client sends it only with the skill's own
// state, a glide only with its glider item, and never for a summon; its other checks are those of any skill
// (MyPC::vfunc_102, CSkillSystem), so a state cast passes the same cast checks as a use (SkillCastCheck).
public class StateSkillCastTests : TriggerTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int SkillId = 20000011;

    private static SkillMetadata StateSkill(ActorState state, int mainType = 0, bool noPlayerSource = false, int[]? maps = null, int skillId = SkillId,
            Func<BeginCondition, BeginCondition>? condition = null) {
        SkillMetadata skill = QuestConditionWiringTests.CastSkill(skillId);
        BeginCondition begin = skill.Data.Condition with { Maps = maps ?? [] };
        return skill with {
            Property = skill.Property with { State = state, MainType = mainType, NoPlayerSource = noPlayerSource },
            Data = skill.Data with { Condition = condition?.Invoke(begin) ?? begin },
        };
    }

    private void Begin(params SkillMetadata[] skills) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        QuestConditionWiringTests.SetSkills(test, skills);
        test.TakeSent();
    }

    // Sends the state cast; true when it was taken (added and no SkillUseFailed answered).
    private bool Send(ActorState state, long uid = 5, long itemUid = 0, int skillId = SkillId) {
        new StateSkillHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(uid);
            writer.WriteInt(0);
            writer.WriteInt(skillId);
            writer.WriteShort(1);
            writer.WriteInt((int) state);
            writer.WriteInt(0);
            writer.WriteLong(itemUid);
        }));
        bool failed = test.TakeSent().Any(p => TestGameSession.Op(p) == SendOp.SkillUseFailed);
        bool added = test.Session.Player.ActiveSkills.Get(uid) != null;
        Assert.That(added, Is.EqualTo(!failed));
        return added;
    }

    [Test]
    public void TheSkillsOwnState_IsTaken_AnotherIsNot() {
        Begin(StateSkill(ActorState.Climb));
        Assert.Multiple(() => {
            Assert.That(Send(ActorState.Glide, uid: 1), Is.False);
            Assert.That(Send(ActorState.Climb, uid: 2), Is.True);
        });
    }

    [Test]
    public void ASkillOfNoState_IsNotTaken() {
        Begin(StateSkill(ActorState.None));
        Assert.That(Send(ActorState.Climb), Is.False);
    }

    // The client's state comes from the main type (FUN_1407e9bd0); the data pack's state stands in without it.
    [Test]
    public void TheMainType_GivesTheState() {
        Begin(StateSkill(ActorState.None, mainType: 13));
        Assert.Multiple(() => {
            Assert.That(Send(ActorState.Hold, uid: 1), Is.False);
            Assert.That(Send(ActorState.Climb, uid: 2), Is.True);
        });
    }

    [Test]
    public void ASummon_IsNotTaken() {
        Begin(StateSkill(ActorState.Summon));
        Assert.That(Send(ActorState.Summon), Is.False);
    }

    [Test]
    public void AGlideWithoutItsItem_IsNotTaken() {
        Begin(StateSkill(ActorState.Glide));
        Assert.That(Send(ActorState.Glide), Is.False);
    }

    [Test]
    public void ADeadPlayer_CastsNoStateSkill() {
        Begin(StateSkill(ActorState.Climb));
        typeof(FieldPlayer).GetField("isDead", Any)!.SetValue(test.Session.Player, true);
        Assert.That(Send(ActorState.Climb), Is.False);
    }

    [Test]
    public void ASkillForOtherFields_IsNotTakenHere() {
        Begin(StateSkill(ActorState.Climb, maps: [2000002]));
        Assert.That(Send(ActorState.Climb), Is.False);
    }

    [Test]
    public void ASkillNoClientPathGives_IsNotTaken() {
        Begin(StateSkill(ActorState.Climb, noPlayerSource: true));
        Assert.That(Send(ActorState.Climb), Is.False);
    }

    // The begin condition tests of FUN_14176a8c0 (SkillCastRules.ConditionRefusal): field 2000001 here has no checkFly,
    // is not on the shadow continent, and the player rides nothing. Skill 20000011 is the client's one unchecked cast.
    private const int OtherSkillId = 20000012;

    [Test]
    public void AFlyFieldSkill_IsNotTakenOnAFieldWithoutFlight_ButTheUncheckedCastIs() {
        Begin(StateSkill(ActorState.Climb, skillId: OtherSkillId, condition: c => c with { OnlyFlyableMap = true }),
            StateSkill(ActorState.Climb, condition: c => c with { OnlyFlyableMap = true }));
        Assert.Multiple(() => {
            Assert.That(Send(ActorState.Climb, uid: 1, skillId: OtherSkillId), Is.False);
            Assert.That(Send(ActorState.Climb, uid: 2), Is.True);
        });
    }

    [Test]
    public void AShadowWorldSkill_IsNotTakenOffTheShadowContinent() {
        Begin(StateSkill(ActorState.Climb, skillId: OtherSkillId, condition: c => c with { OnlyShadowWorld = true }));
        Assert.That(Send(ActorState.Climb, skillId: OtherSkillId), Is.False);
    }

    [Test]
    public void ABattleMountSkill_IsNotTakenOffABattleMount() {
        Begin(StateSkill(ActorState.Climb, skillId: OtherSkillId, condition: c => c with { OnlyOnBattleMount = true }));
        Assert.That(Send(ActorState.Climb, skillId: OtherSkillId), Is.False);
    }

    [Test]
    public void ALivingPlayerWithNoHealth_CastsNothing() {
        Begin(StateSkill(ActorState.Climb, skillId: OtherSkillId));
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 0;
        Assert.That(Send(ActorState.Climb, skillId: OtherSkillId), Is.False);
    }

    [Test]
    public void StateOf_FollowsTheClientsMainTypes() {
        Assert.Multiple(() => {
            Assert.That(SkillCastRules.StateOf(7), Is.EqualTo(ActorState.Glide));
            Assert.That(SkillCastRules.StateOf(10), Is.EqualTo(ActorState.Summon));
            Assert.That(SkillCastRules.StateOf(11), Is.EqualTo(ActorState.SwimDash));
            Assert.That(SkillCastRules.StateOf(12), Is.EqualTo(ActorState.Hold));
            Assert.That(SkillCastRules.StateOf(13), Is.EqualTo(ActorState.Climb));
            Assert.That(SkillCastRules.StateOf(0), Is.EqualTo(ActorState.None));
            Assert.That(SkillCastRules.StateRefuses(ActorState.Glide, 7, ActorState.None, 99), Is.False);
            Assert.That(SkillCastRules.StateRefuses(ActorState.Glide, 0, ActorState.Glide, 0), Is.True);
        });
    }
}
