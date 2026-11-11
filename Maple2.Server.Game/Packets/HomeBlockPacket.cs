using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// HomeBlockManager::OnPacket.
public static class HomeBlockPacket {
    private enum Command : byte {
        Add = 2,
        Remove = 3,
        Load = 4,
        UpdateMemo = 5,
        Result = 6,
    }

    // HomeBlockManager::OnResult: each code shows one notice.
    public enum Error {
        Unknown = 0, // s_home_block_err_unknown
        NoUser = 1, // s_home_block_err_no_user
        SelfBlock = 2, // s_home_block_err_self_block
        AlreadyBlocked = 3, // s_home_block_err_already_block
        MaxBlock = 4, // s_home_block_err_max_block
        EntranceBlocked = 5, // s_home_block_err_enterance_block
        Kicked = 6, // s_home_block_err_kick
    }

    public static ByteWriter Load(ICollection<HomeBlockEntry> entries) {
        var pWriter = Packet.Of(SendOp.HomeBlock);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteInt(entries.Count);
        foreach (HomeBlockEntry entry in entries) {
            pWriter.WriteClass<HomeBlockEntry>(entry);
        }

        return pWriter;
    }

    public static ByteWriter Add(HomeBlockEntry entry) {
        var pWriter = Packet.Of(SendOp.HomeBlock);
        pWriter.Write<Command>(Command.Add);
        pWriter.WriteClass<HomeBlockEntry>(entry);

        return pWriter;
    }

    public static ByteWriter Remove(long id) {
        var pWriter = Packet.Of(SendOp.HomeBlock);
        pWriter.Write<Command>(Command.Remove);
        pWriter.WriteLong(id);

        return pWriter;
    }

    public static ByteWriter UpdateMemo(long id, string memo) {
        var pWriter = Packet.Of(SendOp.HomeBlock);
        pWriter.Write<Command>(Command.UpdateMemo);
        pWriter.WriteLong(id);
        pWriter.WriteUnicodeString(memo);

        return pWriter;
    }

    public static ByteWriter Result(Error error) {
        var pWriter = Packet.Of(SendOp.HomeBlock);
        pWriter.Write<Command>(Command.Result);
        pWriter.WriteInt((int) error);

        return pWriter;
    }
}
