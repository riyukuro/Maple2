using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Core.Packets;

// Sent by the game and by the login (the character list reads the burning levelup values).
// The client keeps two value stores; the byte after the mode picks one (FUN_140930fe0,
// unnamed/unit_14092f420_accountsessionstate.cpp:912), and a Load clears only its own store. The game menu's epic
// restart reads its value from the second store (FUN_14193c000, uigamemenudialog.cpp:1955), as does the time run
// dialog (uitimeruneventdialog.cpp:182, 197); every other reader uses the first.
public static class GameEventUserValuePacket {
    private enum Command : byte {
        Load = 0,
        Update = 1,
    }

    public const byte FirstStore = 0;
    public const byte SecondStore = 1;

    public static byte Store(GameEventUserValueType type) => type is GameEventUserValueType.EpicRestartDone
        or GameEventUserValueType.TimeRunFinalRewardReceived or GameEventUserValueType.TimeRunStepRewardKey
        ? SecondStore : FirstStore;

    public static ByteWriter Load(IList<GameEventUserValue> userValues, byte store = FirstStore) {
        var pWriter = Packet.Of(SendOp.GameEventUserValue);
        pWriter.Write<Command>(Command.Load);
        pWriter.WriteByte(store);
        pWriter.WriteInt(userValues.Count);
        foreach (GameEventUserValue userValue in userValues) {
            pWriter.WriteClass<GameEventUserValue>(userValue);
        }

        return pWriter;
    }

    public static ByteWriter Update(GameEventUserValue userValue) {
        var pWriter = Packet.Of(SendOp.GameEventUserValue);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteByte(Store(userValue.Type));
        pWriter.WriteClass<GameEventUserValue>(userValue);

        return pWriter;
    }
}
