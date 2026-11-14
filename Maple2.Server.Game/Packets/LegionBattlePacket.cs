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
        Remove = 2,
    }

    // The client keeps a spawn time per key (CFieldMetaTable::SetSpawnNpcGuarantee) and the game dictionary shows
    // the first time of a field boss's npc as its guaranteed spawn clock (uigamedictionarydialog.cpp:531). A row
    // without a next spawn (its event over, or no cycle) has no time to show: Load leaves it out and its end is
    // sent as mode 2 (RemoveSpawnNpcGuarantee), not as a time of 0.

    public static ByteWriter Load(IReadOnlyDictionary<int, WorldBossMetadata> bosses) {
        var rows = new List<(WorldBossMetadata Metadata, long Next)>();
        foreach ((int _, WorldBossMetadata metadata) in bosses) {
            long next = WorldBossUtil.ComputeNextSpawnTimestamp(metadata);
            if (next > 0) {
                rows.Add((metadata, next));
            }
        }
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Load);
        pWriter.WriteShort((short) rows.Count);
        foreach ((WorldBossMetadata metadata, long next) in rows) {
            pWriter.WriteInt(metadata.Id);
            pWriter.WriteInt(metadata.NpcIds.Length > 0 ? metadata.NpcIds[0] : 0);
            pWriter.WriteLong(next);
        }
        return pWriter;
    }

    // The next spawn of a row: its time, or its removal when there is none.
    public static ByteWriter Next(int metadataId, int npcId, long nextSpawnTimestamp) {
        return nextSpawnTimestamp > 0 ? Update(metadataId, npcId, nextSpawnTimestamp) : Remove(metadataId);
    }

    public static ByteWriter Remove(int metadataId) {
        ByteWriter pWriter = Packet.Of(SendOp.LegionBattle);
        pWriter.Write<Mode>(Mode.Remove);
        pWriter.WriteInt(metadataId);
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
