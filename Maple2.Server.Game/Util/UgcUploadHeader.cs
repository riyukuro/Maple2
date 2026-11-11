using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Game.Util;

// The head the client writes before the data of an upload (CUgcUpload::Write):
// the kind, the user (CUgcUserProxy) and the upload (CUgcUploadProxy).
public readonly record struct UgcUploadHeader(UgcType Type, long AccountId, long CharacterId, long UgcUid, string UgcName) {
    public static UgcUploadHeader Read(IByteReader packet) {
        var type = (UgcType) packet.ReadByte();
        packet.ReadShort();
        // CUgcUserProxy
        packet.ReadInt(); // user type
        long accountId = packet.ReadLong();
        long characterId = packet.ReadLong();
        packet.ReadUnicodeString(); // object name
        packet.ReadUnicodeString(); // name
        // CUgcUploadProxy
        long ugcUid = packet.ReadLong();
        string ugcName = packet.ReadUnicodeString();
        packet.ReadByte(); // upload type
        packet.ReadByte(); // upload state
        packet.ReadUnicodeString(); // path

        return new UgcUploadHeader(type, accountId, characterId, ugcUid, ugcName);
    }
}
