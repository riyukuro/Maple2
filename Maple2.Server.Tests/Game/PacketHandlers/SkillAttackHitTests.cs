using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A cast's hits as the client sends them (Skill 1 0, the attack point; Skill 1 1, the objects its magic hit; Skill 2,
// the next motion of a cast): a region's hit on an npc it covers pays, one it does not cover or a packet whose facing
// is off pays nothing; an arrow's hit pays inside its step; a point with no target finds a corpse in range; a motion
// point the skill has is taken and one it has not is refused. The ids are made up.
public class SkillAttackHitTests : TriggerTestBase {
    private const int SkillId = 90000961, NpcId = 22000961;
    private const long CastUid = 7, AttackUid = 100;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private FieldNpc npc = null!;

    // A box 300 long and 200 wide ahead of the caster, hostile targets, one motion or two.
    private static SkillMetadataAttack Attack(SkillMetadataArrow? arrow = null) {
        var range = new SkillMetadataRange(SkillRegion.Box, Distance: 300, Height: 200, Width: 200, EndWidth: 0, RotateZDegree: 0,
            RangeAdd: Vector3.Zero, RangeOffset: Vector3.Zero, IncludeCaster: IncludeCasterType.Exclude, ApplyTarget: ApplyTargetType.Hostile,
            CastTarget: SkillTargetType.Target);
        return (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            Point = "", TargetCount = 5, CompulsionTypes = [], Skills = [], SkillsOnDamage = [], Range = range,
            Arrow = arrow ?? (SkillMetadataArrow) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataArrow)),
            Damage = new SkillMetadataDamage(Count: 1, Rate: 1f, HitSpeed: 1, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false,
                Push: new SkillMetadataPush(Type: 0, EaseType: 0, ApplyField: 0, Distance: 0, UpDistance: 0, Down: 0,
                    Fall: false, Duration: 0, Probability: 0, Priority: 0, PriorityHitImmune: 0)),
        };
    }

    // A field with the flat test navmesh and an npc 100 ahead of the player (facing 0 faces -y), the skill cast.
    private void Cast(SkillMetadataAttack attack, Vector3? npcAt = null, int motions = 1) {
        Start();
        test.EnterMap(TestGameSession.Map(2000001) with { Limit = new MapMetadataLimit(0, 0, 0, 0, [], true, true, true, true, true, true, true) });
        typeof(FieldManager).GetField("<Navigation>k__BackingField", Any)!.SetValue(Field, TriggerHallGameTests.FlatNavigation());
        typeof(FieldManager).GetField("<Mobs>k__BackingField", Any)!.SetValue(Field, new ConcurrentDictionary<int, FieldNpc>());
        typeof(FieldManager).GetField("fieldBreakables", Any)!.SetValue(Field, new ConcurrentDictionary<string, FieldBreakable>());
        typeof(FieldManager).GetField("<FieldInstance>k__BackingField", Any)!.SetValue(Field,
            new InstanceFieldMetadata(0, InstanceType.none, 0, false, 0, true, 0, 0, 0, 0));
        // An npc's debug markers (MovementState) are these items.
        test.Register(TestGameSession.Metadata(30000071), TestGameSession.Metadata(50300014), TestGameSession.Metadata(30000084));
        test.SetServerTable(new NpcStatFactorTable(new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyDictionary<int, NpcStatFactorMetadata>>>()));
        typeof(FieldManager).GetField("<NpcMetadata>k__BackingField", Any)!.SetValue(Field, QuestConditionWiringTests.NpcTable(QuestConditionWiringTests.Npc(NpcId)));

        SkillMetadata cast = QuestConditionWiringTests.CastSkill(SkillId);
        SkillMetadataMotion motion = cast.Data.Motions[0] with { Attacks = [attack] };
        SkillMetadataMotion[] all = motions == 1 ? [motion] : [motion, motion];
        QuestConditionWiringTests.SetSkills(test, cast with { Data = cast.Data with { Motions = all } });
        test.Session.Player.Stats.Values[BasicAttribute.Health].Current = 100;
        typeof(Actor<Player>).GetField("Logger", Any)!.SetValue(test.Session.Player, Serilog.Log.ForContext<Player>());
        // The test's player is built without an object id; the field's first npc takes 1.
        typeof(Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(test.Session.Player, 900);
        test.Session.Player.Position = Vector3.Zero;
        test.Session.Player.Rotation = Vector3.Zero;

        Assert.That(Field.NpcMetadata.TryGet(NpcId, out NpcMetadata? metadata), Is.True);
        npc = Field.SpawnNpc(metadata!, npcAt ?? new Vector3(0, -100, 0), Vector3.Zero)!;

        Handle(writer => {
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteInt((int) Field.FieldTick);
            writer.WriteInt(SkillId);
            writer.WriteShort(1);
            writer.WriteByte(0);
            writer.Write(Vector3.Zero);
            writer.Write(-Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.WriteFloat(0);
            writer.WriteInt(0);
            writer.WriteBool(false);
            writer.WriteLong(0);
            writer.WriteLong(0);
        });
        Assert.That(test.Session.Player.ActiveSkills.Get(CastUid), Is.Not.Null, "the cast runs");
        test.TakeSent();
    }

    // The attack point packet (Skill 1 0): attack 100 at attack point 0, its tag naming targetId.
    private void Point(int targetId) {
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(0);
            writer.WriteLong(CastUid);
            writer.WriteByte(0);
            writer.Write(Vector3.Zero);
            writer.Write(-Vector3.UnitY);
            writer.WriteByte(1);
            writer.WriteInt(0);
            writer.WriteLong(AttackUid);
            writer.WriteInt(targetId);
            writer.WriteByte(0);
            writer.WriteBool(false);
        });
    }

    // The target packet (Skill 1 1) of attack 100: the impact, the owner's position (the arrow's step end), the facing
    // and the objects hit.
    private void Target(Vector3 impact, Vector3 owner, Vector3 direction, params int[] ids) {
        Handle(writer => {
            writer.WriteByte(1);
            writer.WriteByte(1);
            writer.WriteLong(CastUid);
            writer.WriteLong(AttackUid);
            writer.Write(impact);
            writer.Write(owner);
            writer.Write(direction);
            writer.WriteByte(0);
            writer.WriteByte((byte) ids.Length);
            writer.WriteInt(0);
            foreach (int id in ids) {
                writer.WriteInt(id);
                writer.WriteByte(0);
            }
        });
    }

    private void Handle(System.Action<Maple2.PacketLib.Tools.ByteWriter> write) {
        new SkillHandler { SkillMetadata = Field.SkillMetadata, TableMetadata = null! }.Handle(test.Session, TestGameSession.Packet(write));
    }

    private SkillRecord Record => test.Session.Player.ActiveSkills.Get(CastUid)!;

    [Test]
    public void ARegionsHitOnAnNpcItCovers_Pays() {
        Cast(Attack());
        Point(npc.ObjectId);
        Target(Vector3.Zero, Vector3.Zero, Vector3.Zero, npc.ObjectId);
        Assert.That(Record.Targets.Keys, Is.EquivalentTo(new[] { npc.ObjectId }));
    }

    [Test]
    public void ARegionsHitOnAnNpcOffTheBox_PaysNothing() {
        Cast(Attack(), npcAt: new Vector3(0, 900, 0));
        Point(npc.ObjectId);
        Target(Vector3.Zero, Vector3.Zero, Vector3.Zero, npc.ObjectId);
        Assert.That(Record.Targets, Is.Empty);
    }

    // The packet's facing far from the cast's and the player's: none of its hits pays.
    [Test]
    public void ARegionPacketFacingAway_PaysNothing() {
        Cast(Attack());
        Point(npc.ObjectId);
        Target(Vector3.Zero, Vector3.Zero, new Vector3(0, 0, 180), npc.ObjectId);
        Assert.That(Record.Targets, Is.Empty);
    }

    // An object the field does not have pays nothing; the rest of the packet still pays.
    [Test]
    public void AnUnknownObject_IsSkippedAndTheRestPays() {
        Cast(Attack());
        Point(npc.ObjectId);
        Target(Vector3.Zero, Vector3.Zero, Vector3.Zero, 999999, npc.ObjectId);
        Assert.That(Record.Targets.Keys, Is.EquivalentTo(new[] { npc.ObjectId }));
    }

    // An arrow's hit on its step from the cast to the npc pays.
    [Test]
    public void AnArrowsHitOnItsStep_Pays() {
        var arrow = (SkillMetadataArrow) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataArrow)) with {
            Collision = new Vector3(50, 50, 50),
        };
        SkillMetadataAttack attack = Attack(arrow) with { MagicPathId = 7 };
        Cast(attack);
        var path = (MagicPath) RuntimeHelpers.GetUninitializedObject(typeof(MagicPath)) with { LifeTime = 1, Velocity = 500, Distance = 300 };
        test.SetTable(new MagicPathTable(new Dictionary<long, IReadOnlyList<MagicPath>> { [7] = [path] }));
        Point(npc.ObjectId);
        Target(new Vector3(0, -90, 0), new Vector3(0, -90, 0), -Vector3.UnitY, npc.ObjectId);
        Assert.That(Record.Targets.Keys, Is.EquivalentTo(new[] { npc.ObjectId }));
    }

    // Skill 2: the cast's next motion point, which the skill has, is taken; one past its motions is refused with the
    // use-failed packet.
    [TestCase((byte) 1, (byte) 1, false)]
    [TestCase((byte) 2, (byte) 0, true)]
    public void ASyncsMotionPoint_IsTakenWhenTheSkillHasIt(byte motionPoint, byte after, bool failed) {
        Cast(Attack(), motions: 2);
        Handle(writer => {
            writer.WriteByte(2);
            writer.WriteLong(CastUid);
            writer.WriteInt(SkillId);
            writer.WriteShort(1);
            writer.WriteByte(motionPoint);
            writer.Write(Vector3.Zero);
            writer.Write(-Vector3.UnitY);
            writer.Write(Vector3.Zero);
            writer.Write(Vector3.Zero);
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteInt(0);
        });
        Assert.Multiple(() => {
            Assert.That(Record.MotionPoint, Is.EqualTo(after));
            Assert.That(test.TakeSent().Count > 0, Is.EqualTo(failed), "the use-failed packet");
        });
    }

    // A point with no target (the npc died since the client's last search) finds the corpse in the attack's range and
    // hits it; a living npc there is left to the client's own target packet.
    [TestCase(true)]
    [TestCase(false)]
    public void APointWithNoTarget_HitsACorpseInRange(bool corpse) {
        Cast(Attack());
        if (corpse) {
            typeof(FieldNpc).GetProperty("IsCorpse", Any)!.SetValue(npc, true);
        }
        Point(0);
        Assert.That(test.TakeSentOps(), corpse ? Does.Contain(SendOp.SkillDamage) : Does.Not.Contain(SendOp.SkillDamage));
    }
}
