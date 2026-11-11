using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ChangeAttributesPacket {
    private enum Command : byte {
        Preview = 1,
        Select = 2,
        // Client: UIItemRemakeOptionDialog::vfunc_79 reads the error as kind 5; kind 4 is a long item uid and an
        // ingredient list.
        Error = 5,
    }

    public static ByteWriter PreviewItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Preview);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter SelectItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Select);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Error(ChangeAttributesError error, bool flag = false) {
        var pWriter = Packet.Of(SendOp.ChangeAttributes);
        pWriter.Write<Command>(Command.Error);
        pWriter.WriteBool(flag); // false: the dialog leaves its waiting state
        pWriter.WriteInt((int) error);

        return pWriter;
    }
}
