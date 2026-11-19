using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class GlobalFactorPacket {
    // The kinds the client reads (QuestDataTable::FUN_14074ee30, questdata.cpp:2361-2373): exp 5, 6 and 7 and meso 0, 1
    // and 2, the epic quest, world quest and navigator mission rates (GlobalFactorTable).
    public static readonly byte[] ExpKinds = [5, 6, 7];
    public static readonly byte[] MesoKinds = [0, 1, 2];

    // CGloabalFactor::ReadRates (gloabalfactor.cpp:202): three exp tables, then three meso tables. Each is its kind (u8,
    // the client's index into its 11 exp or 3 meso kinds) and the rates of levels 1 to 99 (float). The client scales the
    // exp and meso its quest windows show by them; the server's own quest rewards read no rate.
    public static ByteWriter Rates(GlobalFactorTable table) {
        var pWriter = Packet.Of(SendOp.GlobalFactor);
        foreach (byte kind in ExpKinds) {
            pWriter.WriteByte(kind);
            for (int level = 1; level <= GlobalFactorTable.MaxLevel; level++) {
                pWriter.WriteFloat(table.ExpRate(kind, level));
            }
        }
        foreach (byte kind in MesoKinds) {
            pWriter.WriteByte(kind);
            for (int level = 1; level <= GlobalFactorTable.MaxLevel; level++) {
                pWriter.WriteFloat(table.MesoRate(kind, level));
            }
        }

        return pWriter;
    }
}
