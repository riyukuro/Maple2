using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

// The live ban-word list (CDynamicBanWordManager::Read, 0x14034a2c0; DynamicBanWordRules): a byte mode, a long version.
public static class DynamicBanwordPacket {
    // Held from reading the list to sending it, by the entry reply and the GM broadcast, so a channel never sends an
    // older list after a newer one.
    public static readonly object Gate = new();

    // Mode 0: the client drops its list (its version is 0 again).
    public static ByteWriter Clear() {
        var pWriter = Packet.Of(SendOp.DynamicBanword);
        pWriter.WriteByte(DynamicBanWordRules.ModeClear);
        pWriter.WriteLong(0);
        return pWriter;
    }

    // Mode 1: the whole list, a short count and BannedWordData each (CBannedWordFilter::Read, 0x140349bd0).
    public static ByteWriter Load(long version, ICollection<DynamicBanWord> words) {
        var pWriter = Packet.Of(SendOp.DynamicBanword);
        pWriter.WriteByte(DynamicBanWordRules.ModeLoad);
        pWriter.WriteLong(version);
        pWriter.WriteShort((short) words.Count);
        foreach (DynamicBanWord word in words) {
            pWriter.WriteClass<DynamicBanWord>(word);
        }
        return pWriter;
    }

    // The packet bringing a client that holds clientVersion to this list, or null when it holds it already.
    public static ByteWriter? For(long clientVersion, IList<DynamicBanWord> words) {
        return DynamicBanWordRules.ModeFor(clientVersion, words) switch {
            DynamicBanWordRules.ModeClear => Clear(),
            DynamicBanWordRules.ModeLoad => Load(DynamicBanWordRules.Version(words), words),
            _ => null,
        };
    }
}
