using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Tests.Game;

// An npc's skill cast: the use goes to the field with the motion's sequence, the npc stands in the skill state until the
// motion's "end" key, a later motion follows a finished one, and a cast the data cannot play is cancelled.
public class NpcSkillCastTests : NpcCastFixture {
    private static readonly string[] Rig = ["Idle_A", "Attack_Idle_A", "Attack_01_A", "Attack_02_A"];

    [Test]
    public void Cast_UsesTheSkill_AndPlaysTheMotionSequence() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));

        NpcTask task = Cast(50100001);

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.False);
            Assert.That(npc.MovementState.State, Is.EqualTo(ActorState.PcSkill));
            Assert.That(npc.MovementState.CastTask, Is.SameAs(task));
            Assert.That(npc.Animation.PlayingSequence?.Name, Is.EqualTo("Attack_01_A"));
            Assert.That(Sent(SendOp.SkillUse, out _), Is.GreaterThanOrEqualTo(1));
        });
    }

    [Test]
    public void EndKey_CompletesTheCast_AndTheNpcIdles() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        NpcTask task = Cast(50100001);

        npc.MovementState.StateSkillEvent("end");

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.True);
            Assert.That(npc.MovementState.CastTask, Is.Null);
            Assert.That(npc.MovementState.State, Is.Not.EqualTo(ActorState.PcSkill));
        });
    }

    // A skill of two motions: the end of the first starts the second, whose end completes the cast.
    [Test]
    public void EndKey_StartsTheNextMotion_UntilTheLast() {
        StartCaster(Rig, Skill(50100002, ("Attack_01_A", "p0"), ("Attack_02_A", "p1")));
        NpcTask task = Cast(50100002);

        npc.MovementState.StateSkillEvent("end");
        Assert.That(task.IsDone, Is.False);
        Assert.That(npc.Animation.PlayingSequence?.Name, Is.EqualTo("Attack_02_A"));

        npc.MovementState.StateSkillEvent("end");
        Assert.That(task.IsDone, Is.True);
    }

    // A motion that plays nothing after one that does ends the cast with the motion before it.
    [Test]
    public void LaterMotionWithoutASequence_EndsTheCastAfterTheFirst() {
        StartCaster(Rig, Skill(50100003, ("Attack_01_A", "p0"), ("", "")));
        NpcTask task = Cast(50100003);

        npc.MovementState.StateSkillEvent("end");

        Assert.That(task.IsDone, Is.True);
    }

    // A skill the field does not know, or whose sequence the npc's model lacks, is cancelled without the skill state.
    [Test]
    public void UnknownSkill_IsCancelled() {
        StartCaster(Rig);

        NpcTask task = Cast(50109999);

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.True);
            Assert.That(npc.MovementState.State, Is.Not.EqualTo(ActorState.PcSkill));
        });
    }

    [Test]
    public void SequenceTheModelLacks_IsCancelled() {
        StartCaster(Rig, Skill(50100004, ("Attack_09_Z", "p0")));

        NpcTask task = Cast(50100004);

        Assert.That(task.IsDone, Is.True);
    }

    // A skill with no sequence at all fires its first attack with the cast and ends.
    [Test]
    public void SkillWithoutSequences_FiresAtOnce_AndEnds() {
        StartCaster(Rig, Skill(50100005, ("", "p0")));

        NpcTask task = Cast(50100005);

        Assert.That(task.IsDone, Is.True);
    }

    // A keyframe with no cast running cancels the task that is there.
    [Test]
    public void SkillKey_WithoutACast_DoesNothing() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));

        Assert.DoesNotThrow(() => npc.MovementState.StateSkillEvent("end"));
    }

    // Cancelling a running cast drops the skill state.
    [Test]
    public void CancelledCast_LeavesTheSkillState() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        NpcTask task = Cast(50100001);

        task.Cancel();

        Assert.Multiple(() => {
            Assert.That(npc.MovementState.CastTask, Is.Null);
            Assert.That(npc.MovementState.State, Is.Not.EqualTo(ActorState.PcSkill));
        });
    }

    // The key of an attack point fires that attack at the npc's target: a player in the box takes the hit (a damage
    // packet), one beyond it does not.
    [TestCase(950f, true)]
    [TestCase(-1500f, false)]
    public void AttackKey_HitsATargetInTheBox(float playerX, bool hit) {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        test.Session.Player.Position = new System.Numerics.Vector3(playerX, 0, 0);
        Cast(50100001);
        test.TakeSent();

        npc.MovementState.StateSkillEvent("p0");

        Assert.That(Sent(SendOp.SkillDamage, out _) > 0, Is.EqualTo(hit));
    }

    // A key the motion does not name sends nothing; the move keys of a motion without a distance change nothing.
    [Test]
    public void OtherKeys_DoNothing() {
        StartCaster(Rig, Skill(50100001, ("Attack_01_A", "p0")));
        NpcTask task = Cast(50100001);
        test.TakeSent();

        npc.MovementState.StateSkillEvent("move0");
        npc.MovementState.StateSkillEvent("move1");
        npc.MovementState.StateSkillEvent("other");

        Assert.Multiple(() => {
            Assert.That(task.IsDone, Is.False);
            Assert.That(Sent(SendOp.SkillDamage, out _), Is.EqualTo(0));
        });
    }

    private static readonly System.Collections.Generic.Dictionary<string, (string Name, float Time)[]> DashKeys = new() {
        ["Attack_01_A"] = [("move0", 0.5f), ("move1", 1.5f), ("end", 2.5f)],
    };

    // Runs the animation and the npc's movement update in 100 ms steps from the cast's start for the given ms.
    private void Play(int ms) {
        long start = Field.FieldTick;
        for (int elapsed = 0; elapsed <= ms; elapsed += 100) {
            long tick = start + elapsed;
            typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(Field, tick);
            npc.Animation.Update(tick);
            npc.MovementState.Update(tick);
        }
    }

    // KR movedistance (the cast's dash): the client moves the npc by that much along its facing between the sequence's
    // move0 and move1 keys (CSkillRecord::FUN_141759b60 over StateSkill::CalcSkillDuration); the server's position follows.
    [Test]
    public void MoveKeys_DashTheNpcByItsMoveDistance() {
        StartCaster(Rig, DashKeys, Skill(50100010, 300f, ("Attack_01_A", "p0")));
        System.Numerics.Vector3 start = npc.Position;
        System.Numerics.Vector3 front = npc.Transform.FrontAxis;
        Cast(50100010);

        Play(900);
        float midway = System.Numerics.Vector3.Distance(npc.Position, start);
        Play(2600);
        float end = System.Numerics.Vector3.Distance(npc.Position, start);

        Assert.Multiple(() => {
            Assert.That(midway, Is.GreaterThan(0).And.LessThan(300));
            Assert.That(end, Is.EqualTo(300).Within(5));
            Assert.That(System.Numerics.Vector3.Dot(System.Numerics.Vector3.Normalize(npc.Position - start), front), Is.GreaterThan(0.99f));
        });
    }

    // A dash that would end off the navmesh is refused as the client's blocked probe stops its move: the npc stays on the
    // mesh where it was, and the cast still ends.
    [Test]
    public void MoveKeys_DashOffTheNavmesh_IsBlocked() {
        StartCaster(Rig, DashKeys, Skill(50100011, 1_000_000f, ("Attack_01_A", "p0")));
        System.Numerics.Vector3 start = npc.Position;
        Cast(50100011);

        Play(3000);

        Assert.Multiple(() => {
            Assert.That(System.Numerics.Vector3.Distance(npc.Position, start), Is.LessThan(1000));
            Assert.That(npc.Position.X, Is.Not.NaN);
            Assert.That(npc.Navigation!.UpdatePosition(npc.Position), Is.True, "still on the mesh");
        });
    }
}
