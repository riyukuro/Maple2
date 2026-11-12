using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class ChangeAttributesScrollPacket {
    private enum Command : byte {
        UseScroll = 0,
        Preview = 2,
        Select = 3,
        Error = 4,
    }

    public static ByteWriter UseScroll(Item scroll) {
        var pWriter = Packet.Of(SendOp.ChangeAttributesScroll);
        pWriter.Write<Command>(Command.UseScroll);
        pWriter.WriteLong(scroll.Uid);

        return pWriter;
    }

    public static ByteWriter PreviewItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributesScroll);
        pWriter.Write<Command>(Command.Preview);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter SelectItem(Item item) {
        var pWriter = Packet.Of(SendOp.ChangeAttributesScroll);
        pWriter.Write<Command>(Command.Select);
        pWriter.WriteLong(item.Uid);
        pWriter.WriteClass<Item>(item);

        return pWriter;
    }

    public static ByteWriter Error(ChangeAttributesScrollError error, bool flag = false) {
        var pWriter = Packet.Of(SendOp.ChangeAttributesScroll);
        pWriter.Write<Command>(Command.Error);
        // Client UIItemRemakeScrollDialog::OnRemakeError: a byte and an int code; code 0 shows nothing, the byte is
        // only printed in the default string.
        pWriter.WriteBool(flag);
        pWriter.WriteInt((int) error);

        return pWriter;
    }
}
