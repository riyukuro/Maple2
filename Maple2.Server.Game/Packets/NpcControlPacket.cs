using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using System.Numerics;

namespace Maple2.Server.Game.Packets;

public static class NpcControlPacket {
    public const short ANI_JUMP_A = -2;
    public const short ANI_JUMP_B = -3;

    public static ByteWriter Control(params FieldNpc[] npcs) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort((short) npcs.Length);

        foreach (FieldNpc npc in npcs) {
            using var buffer = new PoolByteWriter();
            buffer.NpcBuffer(npc);
            pWriter.WriteShort((short) buffer.Length);
            pWriter.WriteBytes(buffer.ToArray());
        }

        return pWriter;
    }

    // flags=0: no HP bar, no additional effects (used for dead/corpse states)
    public static ByteWriter Dead(FieldNpc npc) =>
        SingleNpcPacket(npc, flags: 0, velocity: default, animSpeed: 100,
            bossTargetId: 0, state: ActorState.None, seqId: -1);

    public static ByteWriter CorpseHit(FieldNpc npc) =>
        SingleNpcPacket(npc, flags: 0, velocity: default, animSpeed: 100,
            bossTargetId: 0, state: ActorState.Hit, seqId: -1);

    public static ByteWriter Talk(FieldNpc npc) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort(1);

        using var buffer = new PoolByteWriter();
        buffer.NpcBuffer(npc, isTalk: true);
        pWriter.WriteShort((short) buffer.Length);
        pWriter.WriteBytes(buffer.ToArray());

        return pWriter;
    }

    private static ByteWriter SingleNpcPacket(FieldNpc npc, byte flags, Vector3S velocity,
            short animSpeed, int bossTargetId, ActorState state, short seqId) {
        var pWriter = Packet.Of(SendOp.NpcControl);
        pWriter.WriteShort(1);

        using var buffer = new PoolByteWriter();
        buffer.WriteNpcEntry(npc, flags, velocity, animSpeed, bossTargetId, state, seqId);
        pWriter.WriteShort((short) buffer.Length);
        pWriter.WriteBytes(buffer.ToArray());
        return pWriter;
    }

    private static void NpcBuffer(this PoolByteWriter buffer, FieldNpc npc, bool isTalk = false) {
        ActorState state;
        short seqId;

        if (isTalk) {
            state = ActorState.Talk;
            seqId = -1;
        } else {
            state = npc.MovementState.State;
            seqId = npc.Animation.PlayingSequence?.Id ?? npc.Animation.IdleSequenceId;
        }

        buffer.WriteNpcEntry(npc,
            flags: 2, // bit-1 (AdditionalEffectRelated), bit-2 (UIHpBarRelated)
            velocity: npc.MovementState.Velocity,
            animSpeed: (short) (npc.Animation.SequenceSpeed * 100),
            bossTargetId: npc.BattleState.TargetId,
            state: state,
            seqId: seqId);

        // Set -1 to continue previous animation
        npc.SequenceId = -1;
    }

    // Client: CNpc::ApplySyncBlock (sequence -2 / -3): byte absolute, then two points and two floats, or one
    // point. Nothing follows the block before the state's own fields.
    internal static void WriteJump(this IByteWriter buffer, bool isAbsolute, Vector3 start, Vector3 end, float angle, float scale) {
        buffer.WriteBool(isAbsolute);
        if (isAbsolute) {
            buffer.Write<Vector3>(start);
            buffer.Write<Vector3>(end);
            buffer.WriteFloat(angle);
            buffer.WriteFloat(scale);
        } else {
            buffer.Write<Vector3>(end); // end offset
        }
    }

    private static void WriteNpcEntry(this PoolByteWriter buffer, FieldNpc npc,
            byte flags, Vector3S velocity, short animSpeed, int bossTargetId,
            ActorState state, short seqId) {
        buffer.WriteInt(npc.ObjectId);
        buffer.WriteByte(flags);
        buffer.Write<Vector3S>(npc.Position);
        buffer.WriteShort((short) (npc.Transform.RotationAnglesDegrees.Z * 10));
        buffer.Write<Vector3S>(velocity);
        buffer.WriteShort(animSpeed);

        if (npc.Value.IsBoss) {
            buffer.WriteInt(bossTargetId);
        }

        buffer.Write<ActorState>(state);
        buffer.WriteShort(seqId);
        buffer.WriteShort(npc.SequenceCounter);

        // Animation (-2 = Jump_A, -3 = Jump_B)
        if (seqId is ANI_JUMP_A or ANI_JUMP_B) {
            buffer.WriteJump(isAbsolute: false, default, default, 0, 0);
        }

        switch (state) {
            case ActorState.Hit:
                buffer.WriteFloat(0); // UnknownF1
                buffer.WriteFloat(0); // UnknownF2
                buffer.WriteFloat(0); // UnknownF3
                buffer.WriteByte(0);  // UnknownB
                break;
            case ActorState.Spawn:
                buffer.WriteInt(npc.SpawnPointId);
                break;
        }
    }
}
