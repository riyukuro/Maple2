using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class SkillDamagePacket {
    private enum Command : byte {
        Target = 0,
        Damage = 1,
        DotDamage = 3,
        Heal = 4,
        Region = 5,
        Tile = 6,
        GrabRelease = 7,
        Unknown8 = 8,
    }

    public static ByteWriter Target(SkillRecord record, ICollection<TargetRecord> targets) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Target);
        pWriter.WriteLong(record.CastUid);
        pWriter.WriteInt(record.Caster.ObjectId);
        pWriter.WriteInt(record.SkillId);
        pWriter.WriteShort(record.Level);
        pWriter.WriteByte(record.MotionPoint);
        pWriter.WriteByte(record.AttackPoint);
        pWriter.Write<Vector3S>(record.Position); // Impact
        pWriter.Write<Vector3>(record.Direction); // Impact
        pWriter.WriteBool(true); // SkillId:10600211 only

        pWriter.WriteByte((byte) targets.Count);
        foreach (TargetRecord target in targets) {
            pWriter.WriteClass<TargetRecord>(target);
        }

        return pWriter;
    }

    public static ByteWriter Damage(DamageRecord record) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Damage);
        pWriter.WriteLong(record.SkillUid);
        pWriter.WriteLong(record.TargetUid);
        pWriter.WriteInt(record.CasterId);
        pWriter.WriteInt(record.SkillId);
        pWriter.WriteShort(record.Level);
        pWriter.WriteByte(record.MotionPoint);
        pWriter.WriteByte(record.AttackPoint);
        pWriter.Write<Vector3S>(record.Position);  // Impact
        pWriter.Write<Vector3S>(record.Direction);

        pWriter.WriteByte((byte) record.Targets.Count);
        foreach (DamageRecordTarget target in record.Targets.Values) {
            pWriter.WriteInt(target.ObjectId);
            pWriter.WriteByte((byte) target.Damage.Count);
            foreach ((DamageType type, long amount) in target.Damage) {
                pWriter.Write<DamageType>(type);
                pWriter.WriteLong(amount);
            }
        }

        return pWriter;
    }

    public static ByteWriter DotDamage(DotDamageRecord record) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.DotDamage);
        pWriter.WriteInt(record.Caster.ObjectId);
        pWriter.WriteInt(record.Target.ObjectId);
        pWriter.WriteInt(record.BuffObjectId); // the client looks the buff up in the target's buff list by this key
        pWriter.Write<DamageType>(record.Type);
        pWriter.WriteLong(record.HpAmount);

        return pWriter;
    }

    public static ByteWriter Heal(HealDamageRecord record, bool animate = true) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Heal);
        pWriter.WriteClass<HealDamageRecord>(record);
        pWriter.WriteBool(animate);

        return pWriter;
    }

    public static ByteWriter Region(DamageRecord record) {
        return Region(record.SkillUid, record.CasterId, record.OwnerId, record.AttackPoint, record.Targets.Values
            .Select(target => new RegionTarget(target.ObjectId, target.Position, target.Direction, target.Damage))
            .ToArray());
    }

    // One target of a region damage packet. Position is the region's point the target was reached from; KMS2
    // sends it cut to whole numbers (not rounded). An attack without damage (damage count 0: a region that only
    // gives a buff or heals) names its targets with no damage entry.
    public readonly record struct RegionTarget(int ObjectId, Vector3 Position, Vector3 Direction,
                                               IReadOnlyList<(DamageType Type, long Amount)> Damage);

    public static ByteWriter Region(long skillUid, int casterId, int ownerId, byte attackPoint, IReadOnlyCollection<RegionTarget> targets) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Region);
        pWriter.WriteLong(skillUid);
        pWriter.WriteInt(casterId);
        pWriter.WriteInt(ownerId);
        pWriter.WriteByte(attackPoint);

        pWriter.WriteByte((byte) targets.Count);
        foreach (RegionTarget target in targets) {
            pWriter.WriteInt(target.ObjectId);
            pWriter.WriteByte((byte) target.Damage.Count);
            Vector3 position = RegionPosition(target.Position);
            pWriter.WriteShort((short) position.X);
            pWriter.WriteShort((short) position.Y);
            pWriter.WriteShort((short) position.Z);
            pWriter.Write<Vector3>(target.Direction);
            foreach ((DamageType type, long amount) in target.Damage) {
                pWriter.Write<DamageType>(type);
                pWriter.WriteLong(amount);
            }
        }

        return pWriter;
    }

    // A region target's position as the packet sends it (and the client reads it): each part cut to a short.
    public static Vector3 RegionPosition(Vector3 position) =>
        new Vector3(CutToShort(position.X), CutToShort(position.Y), CutToShort(position.Z));

    private static short CutToShort(float value) {
        return (short) Math.Clamp(MathF.Truncate(value), short.MinValue, short.MaxValue);
    }

    // The damage of a cube skill (a map block that hurts or gives a buff), one packet for each target.
    // Client: CSceneStateGame::OnSplashDamage reads long skill uid, int skill id, short level, byte count; per
    // target int id, byte entries, 3 shorts, 3 floats, then per entry byte type, long amount.
    // Every KMS2 packet names one target and has a skill uid of 0; the position is the block's.
    public static ByteWriter Tile(int skillId, short level, RegionTarget target) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Tile);
        pWriter.WriteLong(0);
        pWriter.WriteInt(skillId);
        pWriter.WriteShort(level);

        pWriter.WriteByte(1);
        pWriter.WriteInt(target.ObjectId);
        pWriter.WriteByte((byte) target.Damage.Count);
        pWriter.WriteShort(CutToShort(target.Position.X));
        pWriter.WriteShort(CutToShort(target.Position.Y));
        pWriter.WriteShort(CutToShort(target.Position.Z));
        pWriter.Write<Vector3>(target.Direction);
        foreach ((DamageType type, long amount) in target.Damage) {
            pWriter.Write<DamageType>(type);
            pWriter.WriteLong(amount);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::FUN_14171e860 (scenestategame.cpp:7128): int grabber object id, int count, count x int
    // player object id. Each named player still held in a grab (state 43) by that grabber goes back to state 1.
    public static ByteWriter GrabRelease(int grabberId, ICollection<int> playerObjectIds) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.WriteByte((byte) Command.GrabRelease);
        pWriter.WriteInt(grabberId);
        pWriter.WriteInt(playerObjectIds.Count);
        foreach (int objectId in playerObjectIds) {
            pWriter.WriteInt(objectId);
        }

        return pWriter;
    }

    // Client: CSceneStateGame::FUN_14171e9a0 (scenestategame.cpp:7149): long tag uid, byte create; when create:
    // int skill id, short level, int owner object id, int caster object id. create = 0 destroys the tag's model.
    public static ByteWriter Target(long tagUid, bool create, int skillId = 0, short level = 0, int ownerId = 0, int casterId = 0) {
        var pWriter = Packet.Of(SendOp.SkillDamage);
        pWriter.Write<Command>(Command.Unknown8);
        pWriter.WriteLong(tagUid);
        pWriter.WriteBool(create);
        if (create) {
            pWriter.WriteInt(skillId);
            pWriter.WriteShort(level);
            pWriter.WriteInt(ownerId);
            pWriter.WriteInt(casterId);
        }

        return pWriter;
    }
}
