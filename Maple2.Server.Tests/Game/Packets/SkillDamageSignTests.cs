using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// SkillDamage modes 1, 5 and 6 send the health a hit takes negated (CombatLayoutTests.DamageHits and
// RegionDamagePacketTests read the layouts). These cover what stays as it was: the server's record, and the pet
// taming record in every mode.
public class SkillDamageSignTests {
    private const int NpcObjectId = 0x1234;

    private static FieldNpc Npc() {
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        for (Type? type = typeof(FieldNpc); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField("<ObjectId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) {
                field.SetValue(npc, NpcObjectId);
                return npc;
            }
        }
        throw new InvalidOperationException("no ObjectId field");
    }

    private static DamageRecord Record(DamageRecordTarget target) {
        var record = (DamageRecord) RuntimeHelpers.GetUninitializedObject(typeof(DamageRecord));
        typeof(DamageRecord).GetField(nameof(DamageRecord.Targets))!.SetValue(record, new ConcurrentDictionary<int, DamageRecordTarget>());
        record.Targets.TryAdd(target.ObjectId, target);
        return record;
    }

    // The server's own record keeps the health taken positive (ApplyEffects' DealtDamage reads Amount > 0).
    [Test]
    public void RecordKeepsPositiveAmounts() {
        var target = new DamageRecordTarget(Npc());
        target.AddDamage(DamageType.Normal, 1234);
        Assert.That(target.Damage[0].Amount, Is.EqualTo(1234));
        Assert.That(target.HealthTaken, Is.True);
    }

    // The pet taming record's amounts are taming points, not health: mode 1 writes them as they are.
    [Test]
    public void TamingRecordIsNotNegated() {
        var target = new DamageRecordTarget(Npc()) { HealthTaken = false };
        target.AddDamage(DamageType.Normal, -300);
        ByteWriter writer = SkillDamagePacket.Damage(Record(target));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillDamage));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.Skip(8 + 8 + 4 + 4 + 2 + 1 + 1 + 6 + 6);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(NpcObjectId));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DamageType.Normal));
        Assert.That(reader.ReadLong(), Is.EqualTo(-300));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // A region skill's taming hit (FieldSkill calls ApplyDamage on its targets) keeps its sign through mode 5, as
    // through mode 1.
    [Test]
    public void RegionTamingRecordIsNotNegated() {
        var target = new DamageRecordTarget(Npc()) { HealthTaken = false };
        target.AddDamage(DamageType.Normal, -300);
        ByteWriter writer = SkillDamagePacket.Region(Record(target));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillDamage));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        reader.Skip(8 + 4 + 4 + 1);
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(NpcObjectId));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.Skip(6 + 12);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DamageType.Normal));
        Assert.That(reader.ReadLong(), Is.EqualTo(-300));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    // The same for a cube skill's mode 6.
    [Test]
    public void TileTamingTargetIsNotNegated() {
        ByteWriter writer = SkillDamagePacket.Tile(70000008, 1,
            new SkillDamagePacket.RegionTarget(NpcObjectId, Vector3.Zero, Vector3.UnitZ, [(DamageType.Normal, -300)], HealthTaken: false));
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.SkillDamage));
        Assert.That(reader.ReadByte(), Is.EqualTo(6));
        reader.Skip(8 + 4 + 2 + 1 + 4 + 1 + 6 + 12);
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) DamageType.Normal));
        Assert.That(reader.ReadLong(), Is.EqualTo(-300));
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }
}
