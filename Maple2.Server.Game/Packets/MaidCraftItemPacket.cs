using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

// The owner's maid crafts (CManufactureManager::OnPacket, client/ms2/manufacturemanager.cpp), keyed by the maid uid.
// A record is CManufacture (ReadManufacture, commonfiles/manufacture.cpp); the craft dialog's results are read by
// UIManufactureDialog (OnCraftResult, OnReceiveResult, OnCancelResult) as MaidRules.Craft* codes.
public static class MaidCraftItemPacket {
    private enum Command : byte {
        List = 0,
        Add = 1,
        Remove = 3,
        CraftResult = 4,
        ReceiveResult = 6,
        MeretReceiveResult = 7,
        CancelResult = 10,
        Reward = 11,
    }

    // All crafts of the owner's maids (the client clears its list first).
    public static ByteWriter List(ICollection<Maid> maids) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.List);
        pWriter.WriteInt(maids.Count);
        foreach (Maid maid in maids) {
            WriteRecord(pWriter, maid);
        }
        return pWriter;
    }

    public static ByteWriter Add(Maid maid) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.Add);
        WriteRecord(pWriter, maid);
        return pWriter;
    }

    public static ByteWriter Remove(long maidUid) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.Remove);
        pWriter.WriteLong(maidUid);
        return pWriter;
    }

    public static ByteWriter CraftResult(int result) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.CraftResult);
        pWriter.WriteInt(result);
        return pWriter;
    }

    public static ByteWriter ReceiveResult(int result) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.ReceiveResult);
        pWriter.WriteInt(result);
        return pWriter;
    }

    // The answer to a receive at once for merets (mode 7): the client reads it as the receive result.
    public static ByteWriter MeretReceiveResult(int result) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.MeretReceiveResult);
        pWriter.WriteInt(result);
        return pWriter;
    }

    public static ByteWriter CancelResult(int result) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.CancelResult);
        pWriter.WriteInt(result);
        return pWriter;
    }

    // The reward page (FUN_1412e6c30): the product, its count and rarity, the maid exp gained, then the bonus, the
    // jackpot flag and the merat flag. No jackpot receive is handled and no bonus is given, so those three are always 0
    // (the merat flag only changes the bonus line, which a bonus of 0 does not show).
    public static ByteWriter Reward(int itemId, int amount, int rarity, int exp) {
        var pWriter = Packet.Of(SendOp.MaidCraftItem);
        pWriter.WriteByte((byte) Command.Reward);
        pWriter.WriteInt(itemId);
        pWriter.WriteInt(amount);
        pWriter.WriteByte((byte) rarity);
        pWriter.WriteInt(exp);
        pWriter.WriteInt(0);
        pWriter.WriteByte(0);
        pWriter.WriteByte(0);
        return pWriter;
    }

    // CManufacture: m_18 (read nowhere in the client; the maid uid), the maid uid (the key), the recipe, the start
    // time, the lead time (ready at start + lead time) and the total the progress gauge divides by.
    private static void WriteRecord(ByteWriter writer, Maid maid) {
        writer.WriteLong(maid.Uid);
        writer.WriteLong(maid.Uid);
        writer.WriteInt(maid.CraftRecipeId);
        writer.WriteLong(maid.CraftStartTime);
        writer.WriteInt(maid.CraftDuration);
        writer.WriteInt(maid.CraftDuration);
    }
}
