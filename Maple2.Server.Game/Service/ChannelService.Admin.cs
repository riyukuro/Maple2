using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

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
            case AdminRequest.RequestOneofCase.ShutdownNotice:
                return Task.FromResult(ShutdownNotice(request.ShutdownNotice));
            default:
                return Task.FromResult(new AdminResponse());
        }
    }

    private AdminResponse Alert(AdminRequest.Types.Alert alert, long requesterId) {
        var flag = (NoticePacket.Flags) alert.Flags;
        foreach (GameSession session in server.GetSessions()) {
            // A session is listed from its connection, before its player is loaded: it gets nothing.
            if (session.Player is null) {
                continue;
            }
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

    // The channel keeps the shutdown time for the players who enter later (GameSession sends it at entry).
    // KR client only (ShutdownRules); the command refuses under another LOCALE before it gets here.
    private AdminResponse ShutdownNotice(AdminRequest.Types.ShutdownNotice notice) {
        if (!ShutdownRules.Sent(Target.LOCALE)) {
            return new AdminResponse();
        }
        server.ShutdownTime = ShutdownRules.Kept(server.ShutdownTime, notice.Kind, notice.Time);
        foreach (GameSession session in server.GetSessions()) {
            // A session is listed from its connection, before its player is loaded: it gets nothing.
            if (session.Player is null) {
                continue;
            }
            bool staff = session.Player.AdminPermissions.HasFlag(AdminPermissions.GameMaster);
            ByteWriter? packet = ShutdownNoticePacketFor(notice, staff);
            if (packet != null) {
                session.Send(packet);
            }
        }
        return new AdminResponse();
    }

    // Kinds 1 and 2 make the client quit; as with an alert's disconnect, game masters and admins (the requester too)
    // get the message as a system notice and stay (Chosen here).
    internal static ByteWriter? ShutdownNoticePacketFor(AdminRequest.Types.ShutdownNotice notice, bool staff) {
        switch (notice.Kind) {
            case 0:
                return ShutdownNoticePacket.Time(notice.Time);
            case 1:
                return staff ? ShutdownNoticePacket.Notice(notice.Message) : ShutdownNoticePacket.Quit(notice.Message);
            case 2:
                return staff ? ShutdownNoticePacket.Notice(notice.Message) : ShutdownNoticePacket.Maintenance(notice.Message);
            case 3:
                return ShutdownNoticePacket.Notice(notice.Message);
            default:
                return null;
        }
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
        ByteWriter? packet = AccountBlockPacket(session.AccountBlocks, change);
        if (packet != null) {
            session.Send(packet);
        }
        return new AdminResponse();
    }

    // Mode 0 sends the added record as the account holds it now (none when it is already gone), 1 the removal and
    // 2 the new expiry with the change in seconds.
    internal static ByteWriter? AccountBlockPacket(AccountBlock[] blocks, AdminRequest.Types.AccountBlock change) {
        switch (change.Mode) {
            case 0:
                AccountBlock? added = blocks.FirstOrDefault(block => block.Id == change.BlockId);
                return added == null ? null : AdminBlockPacket.Load([added]);
            case 1:
                return AdminBlockPacket.Remove(change.BlockId);
            case 2:
                return AdminBlockPacket.SetExpiry(change.BlockId, change.Kind, change.ExpiryTime, change.Delta);
            default:
                return null;
        }
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

