using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

// The home block list of the buddy window (HomeBlockManager): players an account keeps out of its home.
public class HomeBlockHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.HomeBlock;

    private enum Command : byte {
        Add = 2,
        Remove = 3,
        UpdateMemo = 5,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required BanWordStorage BanWordStorage { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Add:
                HandleAdd(session, packet);
                return;
            case Command.Remove:
                HandleRemove(session, packet);
                return;
            case Command.UpdateMemo:
                HandleUpdateMemo(session, packet);
                return;
        }
    }

    private void HandleAdd(GameSession session, IByteReader packet) {
        long characterId = packet.ReadLong();
        string memo = HomeBlockRules.TrimMemo(packet.ReadUnicodeString());
        if (HomeBlockRules.MemoBanned(memo, BanWordStorage.ContainsBannedWord)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        long? targetAccountId = db.GetHomeBlockAccountId(characterId);
        bool alreadyBlocked = targetAccountId != null && db.IsHomeBlocked(session.AccountId, targetAccountId.Value);
        HomeBlockPacket.Error? error = HomeBlockRules.CheckAdd(session.AccountId, targetAccountId, alreadyBlocked, db.CountHomeBlocks(session.AccountId),
            HomeBlockRules.MaxBlocks(session.ServerTableMetadata.ConstantsTable.MaxBlockCount));
        if (error != null) {
            session.Send(HomeBlockPacket.Result(error.Value));
            return;
        }

        HomeBlockEntry? entry = db.CreateHomeBlock(session.AccountId, characterId, memo);
        if (entry == null) {
            session.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.Unknown));
            return;
        }

        session.Send(HomeBlockPacket.Add(entry));

        // A blocked visitor who is in this account's home right now is sent out, wherever the owner is.
        GameSession? visitor = session.FindSessionByAccountId(entry.AccountId);
        if (visitor?.Field is HomeFieldManager home && home.OwnerId == session.AccountId) {
            visitor.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.Kicked));
            visitor.ReturnField();
        }
    }

    private static void HandleRemove(GameSession session, IByteReader packet) {
        long id = packet.ReadLong();

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.RemoveHomeBlock(session.AccountId, id)) {
            session.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.Unknown));
            return;
        }

        session.Send(HomeBlockPacket.Remove(id));
    }

    private void HandleUpdateMemo(GameSession session, IByteReader packet) {
        long id = packet.ReadLong();
        string memo = HomeBlockRules.TrimMemo(packet.ReadUnicodeString());
        if (HomeBlockRules.MemoBanned(memo, BanWordStorage.ContainsBannedWord)) {
            session.Send(NoticePacket.MessageBox(StringCode.s_ban_check_err_any));
            return;
        }

        using GameStorage.Request db = session.GameStorage.Context();
        if (!db.UpdateHomeBlockMemo(session.AccountId, id, memo)) {
            session.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.Unknown));
            return;
        }

        session.Send(HomeBlockPacket.UpdateMemo(id, memo));
    }
}
