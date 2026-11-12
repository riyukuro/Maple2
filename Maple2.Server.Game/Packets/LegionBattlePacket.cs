using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Packets;

public static class LegionBattlePacket {
    private enum Mode : byte {
        Load = 0,
        Update = 1,
    }

    public static ByteWriter Load(IReadOnlyDictionary<int, WorldBossMetadata> bosses) {
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Load);
        pWriter.WriteShort((short) bosses.Count);
        foreach ((int _, WorldBossMetadata metadata) in bosses) {
            pWriter.WriteInt(metadata.Id);
            pWriter.WriteInt(metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0);
            pWriter.WriteLong(WorldBossUtil.ComputeNextSpawnTimestamp(metadata));
        }
        return pWriter;
    }

    // The client keeps one spawn time per key and only replaces the time of a key it already has; its lookups by
    // npc id take the first key in order. The key is the time event row id, the same one Load sends: a running
    // event number here left the row from Load in place with its old time.
    public static ByteWriter Update(int metadataId, int npcId, long nextSpawnTimestamp) {
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Update);
        pWriter.WriteInt(metadataId);
        pWriter.WriteInt(npcId);
        pWriter.WriteLong(nextSpawnTimestamp);
        return pWriter;
    }
}
