using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class TutorialHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Tutorial;

    public override void Handle(GameSession session, IByteReader packet) {
        // CGuideActionClearTutorial: a guide step that clears the tutorial sends this with no fields.
        // The client reads no reply to it.
        Logger.Debug("Tutorial cleared by {CharacterId}", session.CharacterId);
    }
}
