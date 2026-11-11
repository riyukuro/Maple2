using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class WorldChampionHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.WorldChampion;

    public const byte OpenMode = 8;
    public const byte MoveMode = 9;

    public override void Handle(GameSession session, IByteReader packet) {
        byte mode = packet.ReadByte();
        switch (mode) {
            // The menu entry and the world map icon ask for the running season
            // (uiworldchampionrewarddialog.cpp:277-282). The server runs no season, and every season of
            // WorldChampionBase.xml has ended, so the answer is the client's "no season" notice.
            case OpenMode:
                session.Send(WorldChampionPacket.NoSeason());
                return;
            // The move to the raid field is only offered by the season window, which is never filled.
            case MoveMode:
                return;
        }
    }
}
