using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Common;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Vibrate 0x26: the client sends it from a skill's tag, once per tag and object, only for a shown object, with the
// tag's own skill (WorldService::FUN_141bc3560). The server took any id, skill and count: each packet was relayed to
// the field and counted for the vibrate_object conditions, and the packet's tick and position overwrote the cast's.
public class VibrateTrustTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long CastUid = 77;
    private const int SkillId = 10000001;

    private static FieldVibrateEntity Entity(string id, int index, int hideGroup = 0) {
        return new FieldVibrateEntity(new FieldEntityId(1, 2, id), new Vector3(100, 0, 0), Vector3.Zero, 1, new BoundingBox3(),
            BreakDefense: 99, BreakTick: 0, VibrateIndex: index, HideGroupId: hideGroup, MaterialTag: "wood");
    }

    private sealed class Scene {
        public required TestGameSession Test;
        public required SkillRecord Record;
    }

    // A field with a tree and a rock of hidden group 1; a cast whose attack point 0 has no packet yet.
    private static Scene Start() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        FieldVibrateEntity[] entities = [Entity("tree", 0), Entity("rock", 1, hideGroup: 1)];
        var structure = new FieldAccelerationStructure();
        structure.AddEntities(new Dictionary<Vector3S, List<FieldEntity>>(), new Vector3S(short.MaxValue, 0, 0),
            new Vector3S(0, 0, 0), entities.ToList<FieldEntity>(), entities.Length);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!.SetValue(field, structure);
        typeof(FieldManager).GetField("<Vibrate>k__BackingField", Any)!.SetValue(field, new AiVibrate(entities));

        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = (SkillMetadataDamage) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataDamage)),
        };
        var motion = new SkillMetadataMotion(null!, [attack], new Dictionary<string, byte>());
        var level = (SkillMetadataLevel) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataLevel)) with {
            Motions = [motion],
        };
        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var record = new SkillRecord(new SkillMetadata(SkillId, 1, null, property, null!, level), CastUid, test.Session.Player) {
            Position = new Vector3(10, 20, 0),
        };
        record.Hits.Open(field.FieldTick);
        // The fixture's player is not constructed: it has no skill queue.
        typeof(Maple2.Server.Game.Model.Actor<Maple2.Model.Game.Player>).GetField("<ActiveSkills>k__BackingField", Any)!
            .SetValue(test.Session.Player, new SkillQueue());
        test.Session.Player.ActiveSkills.Add(record);
        test.TakeSent();
        return new Scene { Test = test, Record = record };
    }

    private static void AttackPoint(Scene scene) {
        Assert.That(scene.Record.Hits.TakePoint(0, 0, held: false, count: 1, scene.Test.Session.Field!.FieldTick), Is.True);
    }

    // The Vibrate packets the handler relayed for this packet.
    private static int Vibrate(Scene scene, string entityId, int skillId = SkillId, short level = 1) {
        new VibrateHandler { SkillMetadata = null! }.Handle(scene.Test.Session, TestGameSession.Packet(writer => {
            writer.WriteString(entityId);
            writer.WriteLong(CastUid);
            writer.WriteInt(skillId);
            writer.WriteShort(level);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteInt(123456);
            writer.WriteFloat(5000);
            writer.WriteFloat(5000);
            writer.WriteFloat(0);
        }));
        return scene.Test.TakeSentOps().Count(op => op == SendOp.Vibrate);
    }

    [Test]
    public void OnePacketPerObjectForEachIteration() {
        Scene scene = Start();
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0), "before the attack point's packet");
        AttackPoint(scene);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0), "the tag already sent for the tree");
        });
    }

    [Test]
    public void UnknownOrHiddenObjectIsRefused() {
        Scene scene = Start();
        AttackPoint(scene);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "ghost"), Is.EqualTo(0));
            Assert.That(Vibrate(scene, "rock"), Is.EqualTo(0));
            scene.Test.Session.Field!.Vibrate.Show(1);
            Assert.That(Vibrate(scene, "rock"), Is.EqualTo(1), "shown again");
        });
    }

    [TestCase(10000002, (short) 1)]
    [TestCase(SkillId, (short) 2)]
    public void AnotherSkillIsRefused(int skillId, short level) {
        Scene scene = Start();
        AttackPoint(scene);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree", skillId, level), Is.EqualTo(0));
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1), "the refused one took nothing");
        });
    }

    [Test]
    public void TheCastKeepsItsPosition() {
        Scene scene = Start();
        AttackPoint(scene);
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
        Assert.That(scene.Record.Position, Is.EqualTo(new Vector3(10, 20, 0)));
    }
}
