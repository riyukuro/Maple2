using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

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
    public required WorldClient World { private get; init; }
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
                HandleFindRecruit(session, packet, DungeonRecruitKind.Helper);
                break;
            case Command.FindMember:
                HandleFindRecruit(session, packet, DungeonRecruitKind.Member);
                break;
            case Command.ChaosRaidLobby:
            case Command.ReverseRaidLobby:
            case Command.ColosseumLobby:
            case Command.LapentaLobby:
                HandleHallLobby(session, (byte) command);
                break;
            case Command.ResetUnitedReward:
                HandleResetUnitedReward(session);
                break;
        }
    }

    // UIDungeonRoomInfo::OnUseExtraRewardItem (uidungeonroomdialog.cpp:1502): the dungeon whose extra reward count is
    // bought with its coupon's ticket items, after a confirm box. The client asks only for a room with an
    // extraRewardTicketTableID; the coupon (tag, rates, maximum) comes from the played client's
    // table/dungeonrewardcoupon.xml (PlayedClientRooms). DungeonManager.AddExtraRewardCount charges and answers.
    private static void HandleAddExtraRewardCount(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        session.Dungeon.AddExtraRewardCount(dungeonId);
    }

    // UIDungeonRoomInfo::RequestFindHelper and RequestFindMember (uidungeonroomdialog.cpp:1556/1566): the party
    // leader, or a player without a party, recruits helpers (15) or members (16) for a room. World sends the
    // recruiting notice (DungeonHelp 0x0109) and keeps the recruit open for the chat link (Party 39/43). The client
    // shows the buttons only for a room with a HelperRequireClearCount; a room without one, or with the helper search
    // off, gets the client's text for the search being off (DungeonRecordManagerCli::OnError case 0x1b,
    // dungeonrecordcli.cpp:323). No reply on success; inside the party's cooldown the requester gets Party 40.
    private void HandleFindRecruit(GameSession session, IByteReader packet, DungeonRecruitKind kind) {
        int dungeonId = packet.ReadInt();
        if (!TableMetadata.DungeonRoomTable.Entries.TryGetValue(dungeonId, out DungeonRoomMetadata? metadata)
            || metadata.HelperRequireClearCount == 0 || metadata.DisabledFindHelper) {
            session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_shutdown_find_dungeon_helper, dungeonId));
            return;
        }

        DungeonRecruitRoom room = DungeonRecruitRoom.Of(metadata);
        PartyResponse response;
        try {
            response = World.Party(new PartyRequest {
                RequestorId = session.CharacterId,
                DungeonRecruit = new PartyRequest.Types.DungeonRecruit {
                    Kind = (int) kind,
                    RoomId = room.Id,
                    MinLevel = room.MinLevel,
                    GearScore = room.GearScore,
                    MaxUsers = room.MaxUsers,
                },
            });
        } catch (RpcException ex) {
            // An unreachable World: the party's not-found answer, as for the other party requests.
            Logger.Error(ex, "Dungeon recruit {Kind} for room {RoomId} failed for {CharacterId}", kind, room.Id, session.CharacterId);
            session.Send(PartyPacket.Error(PartyError.s_party_err_not_found));
            return;
        }
        if (response.Error != (int) PartyError.none) {
            session.Send(PartyPacket.Error((PartyError) response.Error));
            return;
        }
        if (response.CooldownMilliseconds > 0) {
            session.Send(PartyPacket.DungeonHelperCooldown(response.CooldownMilliseconds));
        }
    }

    // UIDungeonSearchUnitedReward::SendRoomDungeon (uidungeonhelper.cpp:291): the confirmed reset of the united
    // reward's weekly clear count; no body. The client's refusals (DungeonRecordManagerCli::OnError cases 0x1f to
    // 0x21, dungeonrecordcli.cpp:323) say the reset is once a week for the whole account and only at the weekly
    // limit. The server keeps no account-wide mark of a used reset, and the client does not show whether the reset
    // has a price, so no count is reset: the answer is the client's text for the reset being switched off (0x21).
    private static void HandleResetUnitedReward(GameSession session) {
        session.Send(DungeonRoomPacket.Error(DungeonRoomError.s_room_dungeon_error_shutdown_united_reawrd_reset));
    }

    // The hall buttons of the dungeon search window (DungeonHallRules): the player moves to the group's hall field
    // (a channelScale instance field), or gets the client's refusal. A mode whose hall the server has no map for is
    // dropped with a log line; the client shows those buttons only when the hall's feature is on.
    private void HandleHallLobby(GameSession session, byte mode) {
        int hallFieldId = DungeonHallRules.HallFieldId(mode, session.ServerTableMetadata.ConstantsTable);
        if (hallFieldId == 0 || !session.MapMetadata.TryGet(hallFieldId, out _)) {
            Logger.Debug("RoomDungeon {Mode} from {CharacterId}: no hall field {FieldId}", mode, session.CharacterId, hallFieldId);
            return;
        }
        int dungeonId = session.Field?.DungeonId ?? session.Player.Value.Character.DungeonId;
        DungeonRoomError? error = DungeonHallRules.Error(mode, hallFieldId, session.Field?.MapId ?? 0, dungeonId);
        if (error != null) {
            session.Send(DungeonRoomPacket.Error(error.Value));
            return;
        }
        Character character = session.Player.Value.Character;
        session.Migrate(hallFieldId, beforeMove: () => {
            if (DungeonHallRules.KeepsReturnPosition(session.Field?.MapId ?? 0, character.ReturnMaps.Peek())) {
                character.ReturnPosition = session.Player.Position;
            }
        });
    }

    private void HandleReset(GameSession session) {
        session.Dungeon.Reset();
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool withParty = packet.ReadBool();
        packet.ReadInt(); // portal id
        bool useTicket = packet.ReadBool(); // the extra reward ticket, after AskExtraTicketEnter's confirm box

        session.Dungeon.CreateDungeonRoom(dungeonId, withParty, useTicket);
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
