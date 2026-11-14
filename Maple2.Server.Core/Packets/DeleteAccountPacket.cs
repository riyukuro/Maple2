using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;

namespace Maple2.Server.Core.Packets;

// The answers of the account delete of the character list (UICharacterListDialog::vfunc_79, ops 0x132 and 0x133).
// A non-zero result is shown with GetDeleteAccountErrorText; the character id names the character it is about.
public static class DeleteAccountPacket {
    public static ByteWriter Check(int result, long characterId = 0) {
        var pWriter = Packet.Of(SendOp.CheckDeleteAccount);
        pWriter.WriteInt(result);
        pWriter.WriteLong(characterId);

        return pWriter;
    }

    public static ByteWriter Result(int result) {
        var pWriter = Packet.Of(SendOp.DeleteAccount);
        pWriter.WriteInt(result);

        return pWriter;
    }
}
