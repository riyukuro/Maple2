using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Game.Packets;

public static class GlobalPortalPacket {
    private enum Command : byte {
        Announce = 0,
        Close = 1,
    }

    public static ByteWriter Announce(GlobalPortalMetadata portal, int uid) {
        var pWriter = Packet.Of(SendOp.GlobalPortal);
        pWriter.Write<Command>(Command.Announce);
        pWriter.WriteInt(portal.Id);
        pWriter.WriteInt(uid);
        pWriter.WriteUnicodeString(portal.PopupMessage);
        pWriter.WriteUnicodeString(portal.SoundId);
        // The client reads three captions whatever the number of fields in the row.
        foreach (string caption in GlobalPortalRules.Captions(portal.Entries.Select(entry => entry.Name).ToArray())) {
            pWriter.WriteUnicodeString(caption);
        }

        return pWriter;
    }

    // UIGlobalPortalDialog case 1 (uiglobalportaldialog.cpp:270) reads nothing more: it clears the event id and closes the dialog.
    public static ByteWriter Close() {
        var pWriter = Packet.Of(SendOp.GlobalPortal);
        pWriter.Write<Command>(Command.Close);

        return pWriter;
    }

    // UIGlobalPortalDialog case 3 reads nothing more: the notice s_massive_event_error_cant_enter_dead.
    public static ByteWriter CannotEnterDead() {
        var pWriter = Packet.Of(SendOp.GlobalPortal);
        pWriter.WriteByte(3);

        return pWriter;
    }
}

