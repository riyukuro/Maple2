using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// Gvg 0x006D: the server's option groups, read by CNetworkService::OnGvg (networkservice.cpp:811, @0x14153a8f0): a
// count, then per group its kind (int) and the kind's body. Kinds 1, 7, 25, 34, 42 and 43; any other is an assert.
public static class GvgPacket {
    private const int ContentShutdownKind = 7;

    // Kind 7, the content shutdown flags: a count (short), then the shut contents (short each). The client clears its
    // flags and sets each one listed (CContentShutdownManager::FUN_140cca720, CContentShutdown::Decode,
    // contentshutdown.cpp:26), so every content not listed is on.
    public static ByteWriter ContentShutdown(IReadOnlyCollection<short> contents) {
        var pWriter = Packet.Of(SendOp.Gvg);
        pWriter.WriteInt(1);
        pWriter.WriteInt(ContentShutdownKind);
        pWriter.WriteShort((short) contents.Count);
        foreach (short content in contents) {
            pWriter.WriteShort(content);
        }

        return pWriter;
    }
}
