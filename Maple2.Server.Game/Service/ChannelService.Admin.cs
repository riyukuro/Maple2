using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Service;

public partial class ChannelService {
    public override Task<AdminResponse> Admin(AdminRequest request, ServerCallContext context) {
        switch (request.RequestCase) {
            case AdminRequest.RequestOneofCase.Alert:
                return Task.FromResult(Alert(request.Alert, request.RequesterId));
            case AdminRequest.RequestOneofCase.AddStringBoard:
                return Task.FromResult(AddStringBoard(request.AddStringBoard));
            case AdminRequest.RequestOneofCase.RemoveStringBoard:
                return Task.FromResult(RemoveStringBoard(request.RemoveStringBoard));
            case AdminRequest.RequestOneofCase.AccountBlock:
                return Task.FromResult(UpdateAccountBlock(request.AccountBlock));
            default:
                return Task.FromResult(new AdminResponse());
        }
    }

    private AdminResponse Alert(AdminRequest.Types.Alert alert, long requesterId) {
        var flag = (NoticePacket.Flags) alert.Flags;
        foreach (GameSession session in server.GetSessions()) {
            // Avoid disconnecting the requester
            if (session.CharacterId == requesterId && flag.HasFlag(NoticePacket.Flags.Disconnect) ||
                (session.Player.AdminPermissions == AdminPermissions.Admin ||
                session.Player.AdminPermissions == AdminPermissions.GameMaster)) {
                var moddedFlag = flag & ~NoticePacket.Flags.Disconnect;
                if (moddedFlag == 0) {
                    // No flags left, don't send anything
                    continue;
                }
                session.Send(NoticePacket.Notice(moddedFlag, new InterfaceText(alert.Message), (short) alert.Duration));
                continue;
            }
            session.Send(NoticePacket.Notice(flag, new InterfaceText(alert.Message), (short) alert.Duration));
        }

        return new AdminResponse();
    }

    // The account's session on this channel reloads its restrictions and tells the client (AdminBlock 0x62).
    private AdminResponse UpdateAccountBlock(AdminRequest.Types.AccountBlock change) {
        GameSession? session = server.GetSessionByAccountId(change.AccountId);
        if (session == null) {
            return new AdminResponse();
        }

        using (GameStorage.Request db = gameStorage.Context()) {
            session.AccountBlocks = db.GetAccountBlocks(change.AccountId).ToArray();
        }
        switch (change.Mode) {
            case 0:
                AccountBlock? added = session.AccountBlocks.FirstOrDefault(block => block.Id == change.BlockId);
                if (added != null) {
                    session.Send(AdminBlockPacket.Load([added]));
                }
                break;
            case 1:
                session.Send(AdminBlockPacket.Remove(change.BlockId));
                break;
            case 2:
                session.Send(AdminBlockPacket.SetExpiry(change.BlockId, change.Kind, change.ExpiryTime, change.Delta));
                break;
        }
        return new AdminResponse();
    }

    private AdminResponse AddStringBoard(AdminRequest.Types.AddStringBoard addStringBoard) {
        var gameEvent = new GameEvent(new GameEventMetadata(
            Id: addStringBoard.Id,
            Type: GameEventType.StringBoard,
            StartTime: DateTime.Now,
            EndTime: DateTime.Now.AddYears(1),
            StartPartTime: TimeSpan.Zero,
            EndPartTime: TimeSpan.Zero,
            ActiveDays: [],
            Data: new StringBoard(addStringBoard.Message, 0),
            Value1: string.Empty,
            Value2: string.Empty,
            Value3: string.Empty,
            Value4: string.Empty
        ));

        server.AddEvent(gameEvent);

        return new AdminResponse();
    }

    private AdminResponse RemoveStringBoard(AdminRequest.Types.RemoveStringBoard removeStringBoard) {
        server.RemoveEvent(removeStringBoard.Id);
        return new AdminResponse();
    }
}

