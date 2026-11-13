using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

public static class CurrencyPacket {
    // code: the kind of the gain. The client passes it to the gain text (CSceneStateGame::OnMeso,
    // scenestategame.cpp:7006), which tests it for 0x15 and 0xea61 only (the PC cafe bonus text). KMS2 sends a
    // dungeon clear's meso with the code of its exp (1085, and 1146 for the weekly reward).
    public static ByteWriter UpdateMeso(Currency currency, int code = 0) {
        var pWriter = Packet.Of(SendOp.Meso);
        pWriter.WriteLong(currency.Meso);
        pWriter.WriteInt(code);

        return pWriter;
    }

    // The plain merets. The client (CSceneStateGame::OnMeret, scenestategame.cpp:7025) reads four wallet longs and
    // shows their sum as the merets (FUN_1409304d0 sets them, GetMeratBalance adds them up), then the change for
    // the Merat_Get/Merat_Use text. The red merets are not one of the four: they go in MeretUse (UpdateGameMeret).
    public static ByteWriter UpdateMeret(Currency currency, long delta) {
        var pWriter = Packet.Of(SendOp.Meret);
        pWriter.WriteLong(currency.Meret);
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteLong();
        pWriter.WriteLong(delta);

        return pWriter;
    }

    // The red merets (GameMeret). The client (CSceneStateGame::OnMeretUse, scenestategame.cpp:7055) reads the
    // balance into SetRedMeratBalance and the change for the red meret text (0x2c8 gained, 0x2da used).
    public static ByteWriter UpdateGameMeret(Currency currency, long delta) {
        var pWriter = Packet.Of(SendOp.MeretUse);
        pWriter.WriteLong(currency.GameMeret);
        pWriter.WriteLong(delta);

        return pWriter;
    }

    public static ByteWriter UpdateCurrency(Currency currency, CurrencyType type, long delta, long overflow) {
        long value = type switch {
            CurrencyType.ValorToken => currency.ValorToken,
            CurrencyType.Treva => currency.Treva,
            CurrencyType.Rue => currency.Rue,
            CurrencyType.HaviFruit => currency.HaviFruit,
            CurrencyType.ReverseCoin => currency.ReverseCoin,
            CurrencyType.MentorToken => currency.MentorToken,
            CurrencyType.MenteeToken => currency.MenteeToken,
            CurrencyType.StarPoint => currency.StarPoint,
            CurrencyType.MesoToken => currency.MesoToken,
            _ => 0,
        };

        var pWriter = Packet.Of(SendOp.CurrencyToken);
        pWriter.Write<CurrencyType>(type);
        pWriter.WriteLong(value);
        pWriter.WriteLong(delta);
        pWriter.WriteInt(); // Unknown
        pWriter.WriteLong(overflow);
        return pWriter;
    }
}
