using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

/// <summary>
/// The answers to a recall invite (s2c RecallUser 4). The client never asks for a recall itself; the invite comes
/// from the GM command "recalluser".
/// </summary>
public class RecallUserHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RecallUser;

    private enum Command : byte {
        Answer = 4,
        Busy = 7,
    }

    public override void Handle(GameSession session, IByteReader packet) {
        var command = (Command) packet.ReadByte();
        switch (command) {
            case Command.Answer:
                HandleAnswer(session, packet);
                return;
            case Command.Busy:
                HandleBusy(session, packet);
                return;
        }
    }

    // CNetworkService::SendRecallUserReply (networkservice.cpp:522): int invite id, byte accept, from a button
    // of the confirm box or its timer.
    private void HandleAnswer(GameSession session, IByteReader packet) {
        int inviteId = packet.ReadInt();
        bool accept = packet.ReadByte() != 0;

        // One test-and-clear: the end of the session on another thread cannot answer the same invite too.
        PendingUserRecall? recall = UserRecallUtil.TakeAnswered(session, inviteId);
        if (recall == null) {
            return;
        }
        string name = session.PlayerName;

        if (!accept) {
            UserRecallUtil.TellCaller(session, name, recall.CallerId, false, RecallUserPacket.FAIL_REJECT);
            return;
        }

        // The invite comes only from a GM's command, so the move goes into the caller's field as it is, a home
        // included. The caller must still be on the map the invite was sent from, and the player in a state to leave.
        if (!UserRecallUtil.CanBeMoved(session.Player.IsDead, session.Field is DungeonFieldManager)
            || !session.PlayerInfo.GetOrFetch(recall.CallerId, out PlayerInfo? callerInfo) || !callerInfo.Online || callerInfo.MapId != recall.MapId
            || !PlayerWarpUtil.GoToPlayer(session, callerInfo, intoHome: true)) {
            Logger.Warning("Recall of {CharacterId} to {CallerId} failed", session.CharacterId, recall.CallerId);
            UserRecallUtil.TellCaller(session, name, recall.CallerId, false, RecallUserPacket.FAIL_CANNOT_MOVE);
            return;
        }
        // Sent after the move was started: on a channel change this session is already closing, and the relay
        // does not need it.
        UserRecallUtil.TellCaller(session, name, recall.CallerId, true, 0);
    }

    // CNetworkService::SendRecallUserText (networkservice.cpp:529): the caller's name and byte 4, sent when an
    // invite arrives while the confirm box of an earlier one is open. The server sends no second invite while one
    // is pending and tells that caller itself (s2c 7, result 4), so there is nobody left to tell here.
    private void HandleBusy(GameSession session, IByteReader packet) {
        string callerName = packet.ReadUnicodeString();
        byte result = packet.ReadByte();
        Logger.Debug("RecallUser busy from {CharacterId}: {Caller}, {Result}", session.CharacterId, callerName, result);
    }
}
