using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRoomHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RoomDungeon;

    private enum Command : byte {
        Reset = 1,
        Create = 2,
        EnterLobby = 3,
        ChaosRaidLobby = 4,
        AddExtraRewardCount = 8,
        EnterField = 9,
        ReverseRaidLobby = 12,
        ColosseumLobby = 13,
        LapentaLobby = 14,
        FindHelper = 15,
        FindMember = 16,
        ChangeExpert = 17,
        ResetUnitedReward = 21,
        Favorite = 24,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reset:
                HandleReset(session);
                break;
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.EnterLobby:
                HandleEnter(session, packet);
                break;
            case Command.EnterField:
                HandleEnterField(session);
                break;
            case Command.Favorite:
                HandleFavorite(session, packet);
                break;
            case Command.ChangeExpert:
                HandleChangeExpert(session, packet);
                break;
            case Command.AddExtraRewardCount:
                HandleAddExtraRewardCount(session, packet);
                break;
            case Command.FindHelper:
                HandleFindHelper(session, packet);
                break;
            case Command.FindMember:
                HandleFindMember(session, packet);
                break;
            case Command.ChaosRaidLobby:
            case Command.ReverseRaidLobby:
            case Command.ColosseumLobby:
            case Command.LapentaLobby:
                // Not handled: the lobby button of the dungeon search window, one sub-type per dungeon group; no body.
                Logger.Debug("RoomDungeon {Command} from {CharacterId}", command, session.CharacterId);
                break;
            case Command.ResetUnitedReward:
                HandleResetUnitedReward(session);
                break;
        }
    }

    // UIDungeonRoomInfo::OnUseExtraRewardItem (uidungeonroomdialog.cpp:1502): the dungeon whose extra reward count is
    // bought with its coupon item, after a confirm box. The client asks only for a room with an
    // extraRewardTicketTableID; the coupon's item, rates and maximum are rows of Table/DungeonRewardCoupon.xml, which
    // the data parser does not read. Nothing can be charged, so the answer is the client's own text for a room whose
    // reward count cannot be raised (DungeonRecordManagerCli::OnError case 5, dungeonrecordcli.cpp:323).
    private static void HandleAddExtraRewardCount(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_reward_CantUseExtraReward, dungeonId));
    }

    // UIDungeonRoomInfo::RequestFindHelper (uidungeonroomdialog.cpp:1556): the party leader, or a player without a
    // party, asks for helpers for a dungeon. The helpers come through the recruiting notice and its chat link
    // (Party 39 and 43), which the server never sends, so the answer is the client's text for the helper search being
    // off (DungeonRecordManagerCli::OnError case 0x1b, dungeonrecordcli.cpp:323).
    private static void HandleFindHelper(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_shutdown_find_dungeon_helper, dungeonId));
    }

    // UIDungeonRoomInfo::RequestFindMember (uidungeonroomdialog.cpp:1566): the same request for party members, with
    // the same gate (CanAskHelp) and the same missing recruiting notice. The client has one text for both searches.
    private static void HandleFindMember(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_shutdown_find_dungeon_helper, dungeonId));
    }

    // UIDungeonSearchUnitedReward::SendRoomDungeon (uidungeonhelper.cpp:291): the confirmed reset of the united
    // reward's weekly clear count; no body. The client's refusals (DungeonRecordManagerCli::OnError cases 0x1f to
    // 0x21, dungeonrecordcli.cpp:323) say the reset is once a week for the whole account and only at the weekly
    // limit. The server keeps no account-wide mark of a used reset, and the client does not show whether the reset
    // has a price, so no count is reset: the answer is the client's text for the reset being switched off (0x21).
    private static void HandleResetUnitedReward(GameSession session) {
        session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_shutdown_united_reawrd_reset));
    }

    private void HandleReset(GameSession session) {
        session.Dungeon.Reset();
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool withParty = packet.ReadBool();
        packet.ReadInt(); // portal id
        packet.ReadBool(); // use the extra reward ticket

        session.Dungeon.CreateDungeonRoom(dungeonId, withParty);
    }

    private void HandleEnter(GameSession session, IByteReader packet) {
        int unknown = packet.ReadInt();
        session.Dungeon.EnterLobby();
    }

    private void HandleFavorite(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool favorite = packet.ReadBool();

        if (!session.Dungeon.Records.TryGetValue(dungeonId, out DungeonRecord? record)) {
            return;
        }

        // The client shows the mark from the record's flag, so the record is sent back.
        record.Flag = favorite ? record.Flag | DungeonRecordFlag.Favorite : record.Flag & ~DungeonRecordFlag.Favorite;
        session.Send(DungeonRoomPacket.Update(record));
    }

    private void HandleChangeExpert(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();

        // The same tests the client makes before it shows the button.
        if (!TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata) || metadata.HelperRequireClearCount == 0) {
            return;
        }
        if (!session.Dungeon.Records.TryGetValue(dungeonId, out DungeonRecord? record)) {
            return;
        }
        if (record.Flag.HasFlag(DungeonRecordFlag.Veteran) || record.TotalClears < metadata.HelperRequireClearCount) {
            return;
        }

        record.Flag |= DungeonRecordFlag.Veteran;
        session.Send(DungeonRoomPacket.Update(record));
        session.Send(DungeonRoomPacket.Modify(DungeonRoomModify.ChangeToExpert, dungeonId));
    }

    private void HandleEnterField(GameSession session) {
        // enter first field of dungeon
        session.Dungeon.EnterInitField();
    }
}
