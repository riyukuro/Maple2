using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

// The client's Pvp requests. The server runs no arena queue, no rank duel and no guild PvP, so a queue
// request is answered with the client's own way out of the queue and nothing is ever matched.
public class PvpHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Pvp;

    private enum Command : byte {
        GuildRegister = 1, // uiguilddialog OnRegistPVP
        GuildUnregister = 2,
        Guild3 = 3, // CGuildManager::SendPVPMode3
        Guild4 = 4, // CGuildManager::SendPVPMode4
        ArenaRegister = 9, // UIIndividualPvPDialog, with the picked arena
        ArenaUnregister = 10,
        ArenaJoin = 11, // CPVPManager::SendCancelRequest, after a match
        RankDuelRegister = 14, // UIRankPvPDialog
        RankDuelUnregister = 15,
        RankDuelJoin = 16, // CPvpRankDuelComponent, after a match
        ResultClose = 104, // the result dialogs
        TeamResultClose = 110,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.ArenaRegister:
                packet.ReadInt(); // arena
                session.Send(PvpPacket.ArenaUnregistered());
                return;
            case Command.ArenaUnregister:
            case Command.ArenaJoin:
                session.Send(PvpPacket.ArenaUnregistered());
                return;
            case Command.RankDuelRegister:
            case Command.RankDuelJoin:
                session.Send(PvpPacket.RankDuelMatchFailed());
                return;
            case Command.RankDuelUnregister:
                session.Send(PvpPacket.RankDuelUnregistered());
                return;
            default:
                // Guild PvP has no refusal the client reads here; the result dialogs' close needs no answer.
                return;
        }
    }
}
