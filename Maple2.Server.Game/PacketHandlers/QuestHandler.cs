using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class QuestHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Quest;

    private enum Command : byte {
        Accept = 2,
        Complete = 4,
        DebugQuestList = 5,
        Abandon = 6,
        Expired = 7,
        Exploration = 8,
        Tracking = 9,
        GoToNpc = 12,
        GoToDungeon = 13,
        SkyFortress = 14,
        SkyFortressMission = 15,
        MapleGuide = 16,
        EpicRestart = 17,
        CinematicProgress = 18,
        ResumeDungeon = 19,
        Dispatch = 20,
        RemoteComplete = 24, // Maybe? This is mainly used for Navigator
        CompleteFieldMission = 26,
        ClaimEventQuestReward = 27,
        FindUser = 28,
        ClaimMentoringQuestReward = 29,
        AllianceQuest = 33,
        AllianceAcceptAll = 34,
        AllianceCompleteAll = 35,
        ClaimWeddingQuestReward = 39,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Accept:
                HandleAccept(session, packet);
                break;
            case Command.Complete:
                HandleComplete(session, packet);
                break;
            case Command.Abandon:
                HandleForfeit(session, packet);
                break;
            case Command.Expired:
                HandleExpired(session, packet);
                break;
            case Command.Exploration:
                HandleAddExplorationQuests(session, packet);
                break;
            case Command.Tracking:
                HandleSetTracking(session, packet);
                break;
            case Command.GoToNpc:
                HandleGoToNpc(session, packet);
                break;
            case Command.GoToDungeon:
                HandleGoToDungeon(session, packet);
                break;
            case Command.SkyFortress:
                HandleSkyFortressTeleport(session);
                break;
            case Command.MapleGuide:
                HandleMapleGuide(session, packet);
                break;
            case Command.Dispatch:
                HandleDispatch(session, packet);
                break;
            case Command.RemoteComplete:
            case Command.ClaimEventQuestReward:
            case Command.ClaimMentoringQuestReward:
            case Command.ClaimWeddingQuestReward:
                // The reward buttons of the daily mission, level-up package, time-run event, mentoring and
                // wedding pages. Each page marks a row as taken once the quest's state is completed.
                HandleRemoteComplete(session, packet);
                return;
            case Command.CompleteFieldMission:
                HandleCompleteFieldMission(session, packet);
                break;
            case Command.DebugQuestList: {
                if (!GmCheck.Allow(session, OpCode, (int) command)) {
                    return;
                }

                // Not handled: the client's debug quest commands. Read what it writes.
                int action = packet.ReadInt();
                int count = packet.ReadInt();
                for (int i = 0; i < count; i++) {
                    packet.ReadInt(); // quest id
                    if (action != 0 && action != 3) {
                        int conditions = packet.ReadInt();
                        for (int j = 0; j < conditions; j++) {
                            packet.ReadInt(); // condition value
                        }
                    }
                }
                Logger.Debug("Quest {Command} from {CharacterId}: action {Action}, {Count} quests", command, session.CharacterId, action, count);
                break;
            }
            case Command.CinematicProgress:
                // CQuestProgressCinematicComponent (cinematiccomponent.cpp:611): the talk of a quest in progress
                // ended and the quest has a summonPortal; the player wants its portal again.
                session.Quest.ResummonPortal(packet.ReadInt());
                break;
            case Command.ResumeDungeon:
                HandleReEnterHiddenMap(session, packet);
                break;
            case Command.AllianceQuest:
                // UIAllianceQuestDialog::SendQuestMode33 (unit_140bf60f0_uialliancequestdialog.cpp:705): the
                // id of an alliance quest in progress that the player wants to complete with completion
                // tickets. The server runs no alliance quests, so no quest can be completed this way; the
                // answer is the client's own text for a ticket that could not be used (Quest error 7).
                packet.ReadInt();
                session.Send(QuestPacket.Error(QuestError.s_alliance_quest_completion_ticket_error));
                break;
            case Command.AllianceAcceptAll:
                // UIAllianceQuestDialog::SendQuestMode34 (unit_140bf60f0_uialliancequestdialog.cpp:681): no
                // body; the player confirmed "accept all" in the alliance quest window. The request does not
                // name the alliance and the server keeps none for a player, so no quest is started; the
                // answer is the client's text for a quest that could not be accepted (Quest error 3).
                session.Send(QuestPacket.Error(QuestError.s_quest_error_accept_fail));
                break;
            case Command.AllianceCompleteAll:
                // UIAllianceQuestDialog::SendQuestMode35 (unit_140bf60f0_uialliancequestdialog.cpp:693): no
                // body. Not handled: the client offers it only when an alliance quest in progress can be
                // completed (CanCompleteAllQuests, :632), and no alliance quest can be started here.
                Logger.Debug("Quest {Command} from {CharacterId}", command, session.CharacterId);
                break;
            case Command.EpicRestart:
                // UIGameMenuDialog::SendQuest (uigamemenudialog.cpp:2294): no body. Not handled: the client
                // sends it only while an epic quest restart event runs (FUN_14193c000, :1947), and the server
                // has no such game event.
                Logger.Debug("Quest {Command} from {CharacterId}", command, session.CharacterId);
                break;
            case Command.SkyFortressMission:
                // CQuestManager::SendQuestMode15 (questmanager_2.cpp:1766): one field id, sent from the world
                // map's mission attack box. Not handled: the client sends it only while a quest of that
                // field with an alliance is in progress, and no alliance quest can be started here.
                Logger.Debug("Quest {Command} from {CharacterId}: field {FieldId}", command, session.CharacterId, packet.ReadInt());
                break;
            case Command.FindUser:
                HandleFindUser(session, packet);
                break;
        }
    }

    private static void HandleAccept(GameSession session, IByteReader packet) {
        if (session.Field is null) return;

        int questId = packet.ReadInt();
        int npcObjectId = packet.ReadInt();

        if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata)) {
            return;
        }

        if (metadata.RemoteAccept.Type != QuestRemoteType.None) {
            if (metadata.RemoteAccept.MapId != 0 && metadata.RemoteAccept.MapId != session.Player.Value.Character.MapId) {
                return;
            }

            session.Quest.Start(questId);
            return;
        }

        bool isPostbox = npcObjectId == 0 && metadata.Basic.UsePostbox;
        bool fieldNpcExists = session.Field.Npcs.TryGetValue(npcObjectId, out FieldNpc? _);

        if (!isPostbox && !fieldNpcExists) {
            return;
        }

        session.Quest.Start(questId);
    }

    private void HandleComplete(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest)) {
            return;
        }
        if (!session.Quest.Complete(quest)) {
            Logger.Warning("Could not complete quest {QuestId}", questId);
        }
    }

    private static void HandleForfeit(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest) || !quest.Metadata.Basic.Forfeitable) {
            return;
        }

        if (quest.CompletionCount > 0) {
            quest.State = QuestState.Completed; // ?? how do you revert?
        }

        if (session.Quest.Remove(quest)) {
            session.Send(QuestPacket.Abandon(quest.Id));
        }
    }

    private static void HandleExpired(GameSession session, IByteReader packet) {
        int listSize = packet.ReadInt();
        List<int> questIds = [];
        for (int i = 0; i < listSize; i++) {
            questIds.Add(packet.ReadInt());
        }

        session.Quest.Expired(questIds);
    }

    private static void HandleAddExplorationQuests(GameSession session, IByteReader packet) {
        int listSize = packet.ReadInt();

        for (int i = 0; i < listSize; i++) {
            int questId = packet.ReadInt();

            if (session.Quest.TryGetQuest(questId, out Quest? _)) {
                continue;
            }

            if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata)) {
                continue;
            }

            if (metadata.EventMissionType != QuestEventMissionType.none) {
                continue;
            }

            // Any other quest is accepted from its NPC, item or mail, not through this list.
            if (session.Field == null || !QuestRules.ClientStartsItself(metadata, session.Field.MapId)) {
                continue;
            }

            session.Quest.Start(questId);
        }
    }

    private static void HandleSetTracking(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();
        bool tracking = packet.ReadBool();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest) || quest.State is QuestState.Completed) {
            return;
        }

        session.Send(QuestPacket.SetTracking(questId, tracking));
    }

    private static void HandleGoToNpc(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest) || !quest.Metadata.GoToNpc.Enabled) {
            return;
        }

        session.Send(session.PrepareField(quest.Metadata.GoToNpc.MapId, portalId: quest.Metadata.GoToNpc.PortalId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    // CQuestManager::SendQuestMode19 (questmanager_2.cpp:1459): the quest id. The quest window's re-entry
    // button sends it; the client reads no Quest answer, it waits for the field move.
    private static void HandleReEnterHiddenMap(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (session.Field is null || !session.Quest.TryGetQuest(questId, out Quest? quest)) {
            return;
        }

        QuestMetadataBasic basic = quest.Metadata.Basic;
        if (!QuestRules.CanReEnterHiddenMap(quest.State, basic.UseReEntryHiddenMap, basic.ProgressMaps, session.Field.MapId, session.Player.IsDead)) {
            return;
        }

        session.Send(session.PrepareField(basic.ProgressMaps![0])
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    // QuestUserFindAction::vfunc_2 (actionkeyobject.cpp:3035): the quest id, then the character id of the
    // player the quest action was used on. The answer is the quest's progress (Quest 2).
    private static void HandleFindUser(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();
        long characterId = packet.ReadLong();

        if (session.Field is null || !session.Field.TryGetPlayerById(characterId, out FieldPlayer? target)) {
            return;
        }

        if (!QuestRules.CanFindUser(session.Field.Metadata.Property.Type == MapType.Pvp, characterId == session.CharacterId, target.IsDead,
                Vector3.Distance(session.Player.Position, target.Position),
                session.ServerTableMetadata.ConstantsTable.InteractionDistance, InteractRules.ReachSlack)) {
            return;
        }

        Character character = target.Value.Character;
        EquipManager equips = target.Session.Item.Equips;
        int[] equipItemIds = equips.Gear.Values.Concat(equips.Outfit.Values).Concat(equips.Badge.Values).Select(item => item.Id).ToArray();
        session.Quest.UpdateUserFind(questId, new QuestRules.FoundUser(
            JobCode: (int) character.Job.Code(),
            Level: character.Level,
            Gender: (int) character.Gender,
            FinderGender: (int) session.Player.Value.Character.Gender,
            TitleId: character.Title,
            RideId: target.Session.Ride.Ride?.Metadata.Id ?? 0,
            EquipItemIds: equipItemIds));
    }

    private static void HandleGoToDungeon(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest) || quest.State != QuestState.Started) {
            return;
        }

        session.Dungeon.CreateDungeonRoom(quest.Metadata.GoToDungeon.MapId, false);
    }

    private void HandleMapleGuide(GameSession session, IByteReader packet) {
        int id = packet.ReadInt();

        if (!TableMetadata.LearningQuestTable.Entries.TryGetValue(id, out LearningQuestTable.Entry? metadata)) {
            return;
        }

        if (session.Player.Value.Character.Level < metadata.RequiredLevel ||
            (session.Quest.TryGetQuest(metadata.QuestId, out Quest? quest) && quest.State == QuestState.Completed)) {
            return;
        }

        if (metadata.GoToMapId is Constant.DefaultHomeMapId) {
            session.Migrate(Constant.DefaultHomeMapId, session.AccountId);
            return;
        }

        session.Send(session.PrepareField(metadata.GoToMapId, metadata.GoToPortalId, session.CharacterId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    private void HandleDispatch(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();
        short unknown = packet.ReadShort(); // 3?

        if (!session.Quest.TryGetQuest(questId, out Quest? quest) || quest.State == QuestState.Completed) {
            return;
        }

        if (quest.Metadata.Dispatch == null) {
            return;
        }

        session.Send(session.PrepareField(quest.Metadata.Dispatch.MapId, portalId: quest.Metadata.Dispatch.PortalId == 0 ? -1 : quest.Metadata.Dispatch.PortalId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    private static void HandleRemoteComplete(GameSession session, IByteReader packet) {
        int questId = packet.ReadInt();

        if (!session.Quest.TryGetQuest(questId, out Quest? quest)) {
            return;
        }

        session.Quest.Complete(quest);
    }

    private static void HandleCompleteFieldMission(GameSession session, IByteReader packet) {
        int mission = packet.ReadInt();

        if (session.Config.ExplorationProgress >= mission) {
            return;
        }

        //TODO: If a user completes a field mission and reaches the next goal, if the reward was a stat point, immediately give it. Confirm if same behavior in gms2?
        session.Quest.CompleteFieldMission(mission);
    }

    private void HandleSkyFortressTeleport(GameSession session) {
        if (!session.Quest.TryGetQuest(Constants.FameContentsRequireQuestID, out Quest? quest) || quest.State != QuestState.Completed) {
            return;
        }

        session.Send(session.PrepareField(Constants.FameContentsSkyFortressGotoMapID,
            Constants.FameContentsSkyFortressGotoPortalID)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}
