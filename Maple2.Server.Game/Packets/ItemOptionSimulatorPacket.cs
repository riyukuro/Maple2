using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// CSceneStateGame::OnItemOptionSimulator (scenestategame.cpp:4937), the GM item window's "repeat" tool. Mode 0 clears
// the client's collected rolls, mode 1 adds rolls of one item and rank, mode 2 writes them to ../Debug/itemOptionRepeat.xml.
public static class ItemOptionSimulatorPacket {
    private enum Command : byte {
        Start = 0,
        Options = 1,
        End = 2,
    }

    public static ByteWriter Start() {
        var pWriter = Packet.Of(SendOp.ItemOptionSimulator);
        pWriter.WriteByte((byte) Command.Start);

        return pWriter;
    }

    // int item id, int rank, int count, then count option blocks (CItemOption::Deserialize, the layout of ItemStats).
    public static ByteWriter Options(int itemId, int rarity, ICollection<ItemStats> rolls) {
        var pWriter = Packet.Of(SendOp.ItemOptionSimulator);
        pWriter.WriteByte((byte) Command.Options);
        pWriter.WriteInt(itemId);
        pWriter.WriteInt(rarity);
        pWriter.WriteInt(rolls.Count);
        foreach (ItemStats stats in rolls) {
            pWriter.WriteClass<ItemStats>(stats);
        }

        return pWriter;
    }

    public static ByteWriter End() {
        var pWriter = Packet.Of(SendOp.ItemOptionSimulator);
        pWriter.WriteByte((byte) Command.End);

        return pWriter;
    }
}
