using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class ItemDismantlePacket {
    private enum Command : byte {
        Stage = 1,
        Remove = 2,
        Result = 3,
        Error = 4,
        Preview = 5,
        StageList = 6,
    }

    public static ByteWriter Stage(long itemUid, short slot, int amount) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.Stage);
        pWriter.WriteLong(itemUid);
        pWriter.WriteShort(slot);
        pWriter.WriteInt(amount);

        return pWriter;
    }

    public static ByteWriter Remove(long itemUid) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteLong(itemUid);

        return pWriter;
    }

    public static ByteWriter Result(byte type, IDictionary<int, int> rewards) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.Result);
        pWriter.WriteByte(type); // the type of the open dialog (c2s sub-type 0); the client drops the result if it differs
        pWriter.WriteInt(rewards.Count);
        foreach ((int id, int amount) in rewards) {
            pWriter.WriteInt(id);
            pWriter.WriteInt(amount);
        }

        return pWriter;
    }

    // Client: CItemBreakManager::OnItemDismantlePacket mode 4 (itembreakmanager.cpp:389): int result. 1, 2 and 3 show a
    // notice each, 4 shows none, 5 says that nothing is staged; every value resets the dialog's pending break.
    public static ByteWriter Error(int result) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteInt(result);

        return pWriter;
    }

    // Client: mode 6 (itembreakmanager.cpp:397): int count, count x (short slot, long item uid, int amount).
    // The client fills every slot and plays the stage sound once, where mode 1 plays it per item.
    public static ByteWriter StageList(ICollection<(short Slot, long Uid, int Amount)> staged) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.StageList);
        pWriter.WriteInt(staged.Count);
        foreach ((short slot, long uid, int amount) in staged) {
            pWriter.WriteShort(slot);
            pWriter.WriteLong(uid);
            pWriter.WriteInt(amount);
        }

        return pWriter;
    }

    public static ByteWriter Preview(IDictionary<int, (int Min, int Max)> rewards) {
        var pWriter = Packet.Of(SendOp.ItemDismantle);
        pWriter.Write<Command>(Command.Preview);
        pWriter.WriteInt(rewards.Count);
        foreach ((int id, (int min, int max)) in rewards) {
            pWriter.WriteInt(id);
            pWriter.WriteInt(min);
            pWriter.WriteInt(max);
        }

        return pWriter;
    }
}
