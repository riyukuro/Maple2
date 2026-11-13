using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class JobPacket {
    private enum Command : byte {
        Notify = 0,
        Basic = 1,
        Advance = 2,
        Error = 3,
        Unknown = 7,
        Load = 8,
        Update = 9,
        Reset = 10,
        AutoDistribute = 11,
    }

    // CSceneStateGame::OnJob (scenestategame.cpp:7456): the skill book and OnJobChanged without the advance effect, then for
    // the own player the status and "learned skill" notices. No caller: nothing shows when KMS2 sends it.
    public static ByteWriter Notify(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Notify);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter Basic(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Basic);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter Advance(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Advance);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    // Code 18 (s_err_job_inventory_full) is followed by the count of inventory slots to empty; no other code has a value.
    public static ByteWriter Error(IActor<Player> player, JobError error, int slots = 0) {
        return Error(player.ObjectId, error, slots);
    }

    public static ByteWriter Error(int objectId, JobError error, int slots = 0) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(objectId);
        pWriter.WriteByte((byte) Command.Error);
        pWriter.WriteByte((byte) error);
        if (error == JobError.s_err_job_inventory_full) {
            pWriter.WriteInt(slots);
        }

        return pWriter;
    }

    // CSceneStateGame::OnJob (scenestategame.cpp:7503): the skill book with no job change, then for the own player the status
    // and "learned skill" notices. No caller: nothing shows when KMS2 sends it.
    public static ByteWriter Unknown(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Unknown);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter Load(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter Update(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter Reset(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.Reset);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }

    public static ByteWriter AutoDistribute(IActor<Player> player, SkillInfo skillInfo) {
        var pWriter = Packet.Of(SendOp.Job);
        pWriter.WriteInt(player.ObjectId);
        pWriter.WriteByte((byte) Command.AutoDistribute);
        pWriter.WriteClass<SkillInfo>(skillInfo);

        return pWriter;
    }
}
