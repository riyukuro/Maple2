using System.Diagnostics;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class FieldEnterHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ResponseFieldEnter;

    public override void Handle(GameSession session, IByteReader packet) {
        int fieldKey = packet.ReadInt();
        Debug.Assert(fieldKey == GameSession.FIELD_KEY);

        // The client answers each RequestFieldEnter once, when the field has loaded (scenestategame.cpp:2284, or
        // cubemapinformationservice_3.cpp:5703 once a home's cubes are in). Every field the server prepares sets
        // ChangeMap and the entry sets Connected, so a second answer (a replay) would run the arrival again: the
        // map condition, explore exp, the field's add. It is dropped.
        if (!Accepts(session.State)) {
            Logger.Debug("Dropped a field enter outside a field change ({State})", session.State);
            return;
        }

        session.EnterField();
    }

    public static bool Accepts(SessionState state) => state == SessionState.ChangeMap;
}
