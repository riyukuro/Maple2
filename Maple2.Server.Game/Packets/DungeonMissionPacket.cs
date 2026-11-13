using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// RoomStageDungeonCli::OnPacket reads one mode byte: 0 sets the mission list (count, then per mission its
// id and its type's values), 1 updates missions already in the list, 4 sets the give-up flag. The client
// registers this handler only after RoomStageDungeon mode 0.
public static class DungeonMissionPacket {
    private enum Command : byte {
        Load = 0,
        Update = 1,
        Stage = 3,
        SetAbandon = 4,
        GiveUp = 5,
    }

    public static ByteWriter Load(IDictionary<int, DungeonMission> missions) {
        var pWriter = Packet.Of(SendOp.DungeonMission);
        pWriter.WriteByte((byte) Command.Load);
        pWriter.WriteInt(missions.Count);
        foreach ((int id, DungeonMission mission) in missions) {
            pWriter.WriteClass<DungeonMission>(mission);
        }

        return pWriter;
    }

    public static ByteWriter Update(params DungeonMission[] missions) {
        var pWriter = Packet.Of(SendOp.DungeonMission);
        pWriter.WriteByte((byte) Command.Update);
        pWriter.WriteInt(missions.Length);
        foreach (DungeonMission mission in missions) {
            pWriter.WriteClass<DungeonMission>(mission);
        }

        return pWriter;
    }

    // RoomStageDungeonCli::OnStage (mode 3) reads one byte. The party window offers the give-up vote only
    // while it is 0 and the give-up flag (mode 4) is set (IsStageFlagSet); KMS2 sends 1 with a clear's result.
    public static ByteWriter Stage(byte stage) {
        var pWriter = Packet.Of(SendOp.DungeonMission);
        pWriter.WriteByte((byte) Command.Stage);
        pWriter.WriteByte(stage);

        return pWriter;
    }

    // RoomStageDungeonCli::OnGiveUp (mode 5) reads nothing more and shows the s_party_notify_dungeon_giveup notice.
    public static ByteWriter GiveUp() {
        var pWriter = Packet.Of(SendOp.DungeonMission);
        pWriter.WriteByte((byte) Command.GiveUp);

        return pWriter;
    }

    public static ByteWriter SetAbandon(bool enable) {
        var pWriter = Packet.Of(SendOp.DungeonMission);
        pWriter.WriteByte((byte) Command.SetAbandon);
        pWriter.WriteBool(enable);

        return pWriter;
    }
}
