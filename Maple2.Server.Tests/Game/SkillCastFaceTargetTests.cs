using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Tests.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A skill node's faceTarget 1 or 3 turns the npc to its target at the cast, whether or not the skill has an
// autoTargeting cone; 3 used to fall into the cone check and threw on a skill without one.
public class SkillCastFaceTargetTests : NpcLayoutTestBase {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static SkillMetadata Skill() {
        var motion = new SkillMetadataMotion(new SkillMetadataMotionProperty("Attack_01_A", 1, 0, false), [], new Dictionary<string, byte>());
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            AutoTargeting = null,
            Motions = [motion],
        };
        return new SkillMetadata(50000001, 1, null, null!, null!, level);
    }

    [TestCase(1)]
    [TestCase(3)]
    public void FaceTargetTurnsWithoutAnAutoTargetingCone(int faceTarget) {
        FieldNpc npc = Spawn(boss: false);
        Vector3 before = npc.Transform.FrontAxis;
        // Behind the npc: a turn shows as a front axis reversed.
        test.Session.Player.Position = npc.Position - 300 * Vector3.Normalize(new Vector3(before.X, before.Y, 0));
        MethodInfo face = typeof(MovementState).GetMethod("SkillCastFaceTarget", Any)!;
        Assert.DoesNotThrow(() => face.Invoke(npc.MovementState, [Skill(), (byte) 0, test.Session.Player, faceTarget]));
        var front = Vector2.Normalize(new Vector2(npc.Transform.FrontAxis.X, npc.Transform.FrontAxis.Y));
        var back = -Vector2.Normalize(new Vector2(before.X, before.Y));
        Assert.That(Vector2.Dot(front, back), Is.EqualTo(1f).Within(1e-3f));
    }
}
