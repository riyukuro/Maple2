using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DynamicBanwordHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.DynamicBanword;

    public override void Handle(GameSession session, IByteReader packet) {
        // CSceneStateGame sends the version of the client's ban-word list on server entry (0 when it
        // has none). The reply would carry a new list or changes to it; this server keeps no list, and
        // without a reply the client filters with none, so nothing is sent.
        long version = packet.ReadLong();
        Logger.Debug("Dynamic ban-word version from {CharacterId}: {Version}", session.CharacterId, version);
    }
}
