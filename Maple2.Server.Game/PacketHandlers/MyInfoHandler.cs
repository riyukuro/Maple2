using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class MyInfoHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.MyInfo;

    private enum Command : byte {
        SetMotto = 0,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.SetMotto:
                HandleSetMotto(session, packet);
                return;
        }
    }

    private void HandleSetMotto(GameSession session, IByteReader packet) {
        string motto = MyInfoRules.ReadMotto(packet);
        if (motto.Length > Constant.MaxMottoLength) {
            session.Send(MyInfoPacket.Error(session.Player.ObjectId, $"The motto can only be up to {Constant.MaxMottoLength} letters long."));
            return;
        }

        // The client's own refusal for a banned motto: it shows s_ban_check_err_any_word with the text.
        if (MyInfoRules.MottoBanned(motto, BanWordStorage.ContainsBannedWord)) {
            session.Send(MyInfoPacket.BanMotto(session.Player.ObjectId, motto));
            return;
        }

        session.Player.Value.Character.Motto = motto;
        session.Field?.Broadcast(MyInfoPacket.UpdateMotto(session.Player));

        session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = session.AccountId,
            CharacterId = session.CharacterId,
            Motto = motto,
            Async = true,
        });
        session.Player.Flag |= PlayerObjectFlag.Motto;
    }
}
