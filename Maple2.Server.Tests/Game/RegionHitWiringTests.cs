using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Game;

// A region's hit on an npc and a player, through the code the fire runs: the region's damage record
// (FieldSkill.Damage), each target's entry (DamageRecordTarget.For, as Actor.ApplyDamage makes it), the region damage
// packet the clients read (SkillDamagePacket.Region) and the npc's hit source (FieldNpc.HitSourceOf). The npc moves
// from what the packet carries: the fire point (types 1, 2 and 4) and the direction from it (type 5).
public class RegionHitWiringTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void Place<T>(Actor<T> actor, int objectId, Vector3 position) {
        typeof(Actor<T>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(actor, objectId);
        if (actor.Transform == null) {
            // The fixture's actors are not constructed.
            typeof(Actor<T>).GetField("<Transform>k__BackingField", Any)!.SetValue(actor, new Transform());
        }
        actor.Position = position;
    }

    // The burst of a magnetic bomb (10600062): a region placed where the bomb hit, its attack pulls to the fire point.
    private static SkillMetadataAttack Attack(int pushType) {
        return (SkillMetadataAttack) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataAttack)) with {
            CompulsionTypes = [],
            Damage = new SkillMetadataDamage(Count: 1, Rate: 1.7f, HitSpeed: 1, HitDelay: 0, IsConstDamage: false, Value: 0,
                DamageByTargetMaxHp: 0, SuperArmorBreak: false,
                Push: new SkillMetadataPush(Type: pushType, EaseType: 1, ApplyField: 1, Distance: 100, UpDistance: 0, Down: 0,
                    Fall: false, Duration: 0.4f, Probability: 1, Priority: 0, PriorityHitImmune: 0)),
        };
    }

    private static (Vector3 Position, Vector3 Direction) ReadTarget(ByteReader reader) {
        reader.ReadInt();
        byte damageCount = reader.ReadByte();
        var position = new Vector3(reader.ReadShort(), reader.ReadShort(), reader.ReadShort());
        var direction = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
        for (int i = 0; i < damageCount; i++) {
            reader.ReadByte();
            reader.ReadLong();
        }
        return (position, direction);
    }

    [TestCase(4)]
    [TestCase(5)]
    public void TheNpcMovesFromWhatThePacketCarries(int pushType) {
        var test = new TestGameSession();
        test.Start();
        var other = new TestGameSession(test.Options);
        other.Start();
        FieldManager field = test.Session.Field!;
        FieldPlayer caster = test.Session.Player;
        Place(caster, 1, new Vector3(500, 500, 0));
        FieldPlayer player = other.Session.Player;
        Place(player, 2, new Vector3(10, 350, 0));
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        Place(npc, 3, new Vector3(150.7f, 20.3f, 0));

        var property = (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty));
        var skill = new SkillMetadata(10600062, 1, null, property, null!, null!);
        // A region with two points facing 90 degrees: each target is reached from its nearer point.
        var region = new FieldSkill(field, 900, caster, skill, 1100, new Vector3(100, 0, 0), new Vector3(0, 300, 0)) {
            Position = new Vector3(50, 150, 0),
            Rotation = new Vector3(0, 0, 90),
        };
        SkillMetadataAttack attack = Attack(pushType);
        DamageRecord damage = region.Damage(new SkillRecord(skill, 0, caster), attack);
        DamageRecordTarget npcHit = DamageRecordTarget.For(npc, caster, damage, attack);
        damage.Targets.TryAdd(npc.ObjectId, npcHit);
        damage.Targets.TryAdd(player.ObjectId, DamageRecordTarget.For(player, caster, damage, attack));

        ByteWriter packet = SkillDamagePacket.Region(damage);
        var reader = new ByteReader(packet.Buffer, 0);
        reader.ReadShort(); // opcode
        reader.ReadByte(); // command
        reader.ReadLong();
        Assert.That(reader.ReadInt(), Is.EqualTo(caster.ObjectId));
        Assert.That(reader.ReadInt(), Is.EqualTo(region.ObjectId));
        reader.ReadByte();
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        var sent = new Dictionary<int, (Vector3 Position, Vector3 Direction)>();
        foreach (int id in damage.Targets.Keys) {
            sent[id] = ReadTarget(reader);
        }
        Assert.That(reader.Position, Is.EqualTo(packet.Length));

        NpcControlRules.HitSource source = FieldNpc.HitSourceOf(caster, damage, npcHit, attack);
        Vector3 npcDirection = Vector3.Normalize(new Vector3(50.7f, 20.3f, 0));
        Vector3 playerDirection = Vector3.Normalize(new Vector3(10, 50, 0));
        Assert.Multiple(() => {
            Assert.That(sent[npc.ObjectId].Position, Is.EqualTo(new Vector3(100, 0, 0)));
            Assert.That(Vector3.Distance(sent[npc.ObjectId].Direction, npcDirection), Is.LessThan(0.0001f));
            Assert.That(sent[player.ObjectId].Position, Is.EqualTo(new Vector3(0, 300, 0)));
            Assert.That(Vector3.Distance(sent[player.ObjectId].Direction, playerDirection), Is.LessThan(0.0001f));
            // the npc reads the same entry
            Assert.That(source.Forward, Is.EqualTo(sent[npc.ObjectId].Direction));
            Assert.That(source.Origin, Is.EqualTo(sent[npc.ObjectId].Position));
            Assert.That(source.Attacker, Is.EqualTo(caster.Position));
        });
        Vector3 offset = NpcControlRules.PushOffset(pushType, source, npc.Position, 100);
        if (pushType == 4) {
            // pulled to the fire point, not to the caster
            Assert.That(Vector3.Distance(npc.Position + offset, new Vector3(100, 0, 0)), Is.LessThan(0.01f));
        } else {
            Assert.That(Vector3.Distance(offset, npcDirection * 100), Is.LessThan(0.01f));
        }
    }
}
