using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The client (CSceneStateGame::OnFindFields) reads int count + count map ids and appends them to its visited maps,
// the list ServerEnter sends: the world map's explored fields, the air taxi pay mode and the play-once loading movies.
public static class FindFieldsPacket {
    public static ByteWriter Add(ICollection<int> mapIds) {
        var pWriter = Packet.Of(SendOp.FindFields);
        pWriter.WriteInt(mapIds.Count);
        foreach (int mapId in mapIds) {
            pWriter.WriteInt(mapId);
        }
        return pWriter;
    }
}
