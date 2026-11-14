using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
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

    private static FieldVibrateEntity Entity(string id, int index, int hideGroup = 0, Vector3 shape = default) {
        return new FieldVibrateEntity(new FieldEntityId(1, 2, id), new Vector3(100, 0, 0), Vector3.Zero, 1, new BoundingBox3(),
            BreakDefense: 99, BreakTick: 0, VibrateIndex: index, HideGroupId: hideGroup, MaterialTag: "wood", ShapeDimensions: shape);
    }

    // A box region 300 ahead (ImpactReach 1050), the fixture's attack.
    private static readonly SkillMetadataRange Box = new(SkillRegion.Box, 300, 150, 150, 0, 0, default, default,
        IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);
    private static readonly SkillMetadataArrow NoArrow = new(false, false, false, SkillTargetType.None, BounceType.None, 0, 0,
        false, default, default, 0);

    private sealed class Scene {
        public required TestGameSession Test;
        public required SkillRecord Record;
    }

    // A field with a tree, a rock of hidden group 1 and a big tree (ShapeDimensions 1000 each way); a cast whose
    // attack point 0 has no packet yet.
    private static Scene Start() {
        var test = new TestGameSession();
        test.Start();
        FieldManager field = test.Session.Field!;
        FieldVibrateEntity[] entities = [Entity("tree", 0), Entity("rock", 1, hideGroup: 1), Entity("bigtree", 2, shape: new Vector3(1000, 1000, 1000))];
        var structure = new FieldAccelerationStructure();
        structure.AddEntities(new Dictionary<Vector3S, List<FieldEntity>>(), new Vector3S(short.MaxValue, 0, 0),
            new Vector3S(0, 0, 0), entities.ToList<FieldEntity>(), entities.Length);
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", Any)!.SetValue(field, structure);
        typeof(FieldManager).GetField("<Vibrate>k__BackingField", Any)!.SetValue(field, new AiVibrate(entities));

        var attack = (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = (SkillMetadataDamage) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataDamage)),
            Range = Box, Arrow = NoArrow,
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
        test.SetTable(new MagicPathTable(new Dictionary<long, IReadOnlyList<MagicPath>>()));
        // The fixture's player is not constructed: it has no skill queue.
        typeof(Maple2.Server.Game.Model.Actor<Maple2.Model.Game.Player>).GetField("<ActiveSkills>k__BackingField", Any)!
            .SetValue(test.Session.Player, new SkillQueue());
        test.Session.Player.ActiveSkills.Add(record, test.Session.Field!.FieldTick);
        test.TakeSent();
        return new Scene { Test = test, Record = record };
    }

    // The attack point's packet, declaring tags attacks (one tag per attack; 0 when nothing was picked).
    private static void AttackPoint(Scene scene, int tags = 1) {
        Assert.That(scene.Record.Hits.TakePoint(0, 0, held: false, count: tags, scene.Test.Session.Field!.FieldTick), Is.True);
        for (int i = 0; i < tags; i++) {
            scene.Record.Hits.Declare(1000 + i, 0, 0);
        }
    }

    // The Vibrate packets the handler relayed for this packet.
    private static int Vibrate(Scene scene, string entityId, int skillId = SkillId, short level = 1, byte motionPoint = 0, long castUid = CastUid) {
        new VibrateHandler { SkillMetadata = null! }.Handle(scene.Test.Session, TestGameSession.Packet(writer => {
            writer.WriteString(entityId);
            writer.WriteLong(castUid);
            writer.WriteInt(skillId);
            writer.WriteShort(level);
            writer.WriteByte(motionPoint);
            writer.WriteByte(0);
            writer.WriteInt(123456);
            writer.WriteFloat(5000);
            writer.WriteFloat(5000);
            writer.WriteFloat(0);
        }));
        return scene.Test.TakeSentOps().Count(op => op == SendOp.Vibrate);
    }

    [Test]
    public void OnePacketPerObjectForOneTag() {
        Scene scene = Start();
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0), "before the attack point's packet");
        AttackPoint(scene);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0), "the tag already sent for the tree");
        });
    }

    // Each tag of the attack point sends once for the tree: two tags, two packets.
    [Test]
    public void OnePacketPerObjectForEachTag() {
        Scene scene = Start();
        AttackPoint(scene, tags: 2);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0));
        });
    }

    // An attack point packet with no tag places nothing that could meet the tree.
    [Test]
    public void NoTagNoPacket() {
        Scene scene = Start();
        AttackPoint(scene, tags: 0);
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0));
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

    // The tag's area meets the object's box, so the object lies within the attack's reach of the cast or the player:
    // the tree 1400 past the box's reach (1050 + margin 600) is taken, 4900 past is refused. Before, any object of the
    // map took the packet.
    [TestCase(1500f, ExpectedResult = 1)]
    [TestCase(5000f, ExpectedResult = 0)]
    public int AnObjectPastTheAttacksReachIsRefused(float away) {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Vibrate(scene, "tree");
    }

    // A big tree's box (ShapeDimensions 1000 each way) is hit at its side, past the reach from its position: taken.
    [TestCase(3000f, ExpectedResult = 1)]
    [TestCase(6000f, ExpectedResult = 0)]
    public int ABigObjectHitAtItsSideIsTaken(float away) {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(away, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(away, 0, 0);
        AttackPoint(scene);
        return Vibrate(scene, "bigtree");
    }

    // A motion point the skill lacks has no attack whose reach the tree could be in: refused, the tag kept.
    [Test]
    public void AMotionPointTheSkillLacksIsRefused() {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(8000, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(8000, 0, 0);
        AttackPoint(scene);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree", motionPoint: 99), Is.EqualTo(0));
            scene.Test.Session.Player.Position = new Vector3(500, 0, 0);
            Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1), "the refused one took nothing");
        });
    }

    // The cast moved since (a dash): the object near the player now is taken.
    [Test]
    public void AnObjectNearThePlayerNowIsTaken() {
        Scene scene = Start();
        scene.Record.CastPosition = new Vector3(8000, 0, 0);
        scene.Test.Session.Player.Position = new Vector3(500, 0, 0);
        AttackPoint(scene);
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
    }

    // The player cast three more skills before the cast's magic met the tree: the pending queue dropped the cast, and
    // the client still sends the tag with its uid, so the tree takes it. Before, the packet was refused.
    [Test]
    public void ACastPushedOutOfThePendingQueueStillTakesTheTag() {
        Scene scene = Start();
        AttackPoint(scene);
        for (long uid = CastUid + 1; uid <= CastUid + 3; uid++) {
            scene.Test.Session.Player.ActiveSkills.Add(new SkillRecord(scene.Record.Metadata, uid, scene.Test.Session.Player),
                scene.Test.Session.Field!.FieldTick);
        }
        Assert.That(scene.Test.Session.Player.ActiveSkills.Get(CastUid), Is.Null, "no longer pending");
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(1));
    }

    // Past the cast's keep time the tag is refused.
    [Test]
    public void ACastPastItsKeepTimeIsRefused() {
        Scene scene = Start();
        AttackPoint(scene);
        FieldManager field = scene.Test.Session.Field!;
        typeof(FieldManager).GetField("<FieldTick>k__BackingField", Any)!.SetValue(field, field.FieldTick + SkillHitRules.HitWindowMs + 1000);
        Assert.That(Vibrate(scene, "tree"), Is.EqualTo(0));
    }

    // A region of the player's at a point, of the fixture's skill, that has fired fires times.
    private static FieldSkill Region(Scene scene, Vector3 point, int fires) {
        FieldManager field = scene.Test.Session.Field!;
        var skills = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", Any)!.SetValue(field, skills);
        typeof(FieldManager).GetField("removedSkills", Any)!.SetValue(field, new ConcurrentDictionary<int, (FieldSkill, long)>());
        var region = new FieldSkill(field, 900, scene.Test.Session.Player, scene.Record.Metadata, 1000, point);
        Fired(region, fires);
        skills[900] = region;
        return region;
    }

    private static void Fired(FieldSkill region, int fires) {
        typeof(FieldSkill).GetField("<Fires>k__BackingField", Any)!.SetValue(region, fires);
    }

    // A region skill's hit makes its tags with uid 0 (as for Breakable): each fire of the player's region sends once
    // for the tree. Before, uid 0 was refused as an unknown cast.
    [Test]
    public void ARegionsTagIsTakenOncePerFire() {
        Scene scene = Start();
        FieldSkill region = Region(scene, new Vector3(300, 0, 0), fires: 0);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(0), "not fired yet");
            Fired(region, 1);
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(1));
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(0), "the fire already sent for the tree");
            Assert.That(Vibrate(scene, "bigtree", castUid: 0), Is.EqualTo(1), "another object");
            Fired(region, 2);
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(1), "the next fire");
        });
    }

    // A region whose attack does not reach the tree, another skill or level, a point the skill lacks: refused.
    [Test]
    public void ARegionsTagPastItsReachOrOfAnotherSkillIsRefused() {
        Scene scene = Start();
        Region(scene, new Vector3(5000, 0, 0), fires: 5);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(0), "past the reach");
            Assert.That(Vibrate(scene, "tree", skillId: 10000002, castUid: 0), Is.EqualTo(0));
        });
        Region(scene, new Vector3(300, 0, 0), fires: 5);
        Assert.Multiple(() => {
            Assert.That(Vibrate(scene, "tree", level: 2, castUid: 0), Is.EqualTo(0));
            Assert.That(Vibrate(scene, "tree", motionPoint: 3, castUid: 0), Is.EqualTo(0));
            Assert.That(Vibrate(scene, "tree", castUid: 0), Is.EqualTo(1));
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
