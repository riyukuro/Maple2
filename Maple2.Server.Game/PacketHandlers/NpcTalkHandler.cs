using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class NpcTalkHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.NpcTalk;

    private enum Command : byte {
        Close = 0,
        Talk = 1,
        Continue = 2,
        EnchantWithNpc = 4,
        Enchant = 6,
        Quest = 7,
        AcceptAllianceQuest = 8,
        TalkAlliance = 9,
        Custom = 11,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required NpcMetadataStorage NpcMetadata { private get; init; }
    public required ScriptMetadataStorage ScriptMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Close:
                HandleClose(session);
                return;
            case Command.Talk:
                HandleTalk(session, packet);
                return;
            case Command.Continue:
                HandleContinue(session, packet);
                return;
            case Command.EnchantWithNpc:
                HandleEnchantWithNpc(session, packet);
                return;
            case Command.Enchant:
                HandleEnchant(session, packet);
                return;
            case Command.Quest:
                HandleQuest(session, packet);
                return;
            case Command.AcceptAllianceQuest:
                HandleAcceptAllianceQuest(session, packet);
                return;
            case Command.TalkAlliance:
                HandleTalkAlliance(session);
                return;
            case Command.Custom:
                return;
        }
    }

    private void HandleClose(GameSession session) {
        session.NpcScript?.Npc?.StopTalk();
        session.NpcScript = null;
        session.Shop.ClearActiveShop();
    }

    private void HandleTalk(GameSession session, IByteReader packet) {
        // Each early return logs at Debug, so a run shows whether a talk arrived and which check dropped it.
        if (session.Field is null) {
            Logger.Debug("Talk dropped: no field");
            return;
        }
        int objectId = packet.ReadInt();
        if (!session.Field.Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
            Logger.Debug("Talk to object {ObjectId} dropped: no such npc in map {MapId}", objectId, session.Field.MapId);
            return;
        }
        // The client hides an npc whose quest mask fails (QuestMaskRules.Visible) and sends no talk to it.
        if (!session.Quest.MaskShows(session.Field.MapId, npc.Value.Id)) {
            Logger.Debug("Talk to npc {ObjectId} ({NpcId}) dropped: quest mask hides it", objectId, npc.Value.Id);
            return;
        }
        // Nor does it talk to an npc out of its talk reach (InteractRules.TalkReach).
        float radius = npc.Value.Metadata.Property?.Capsule?.Radius ?? 0f;
        if (!InteractRules.InReach(session.Player.Position, npc.Position, InteractRules.TalkReach(Constants.TalkableDistance, radius))) {
            Logger.Debug("Talk to npc {ObjectId} ({NpcId}) dropped: out of reach, player {Player}, npc {Npc}",
                objectId, npc.Value.Id, session.Player.Position, npc.Position);
            return;
        }

        BeginTalk(session, npc);
    }

    /// <summary>
    /// Starts the talk with an npc: the player's click, or a trigger's talk_npc (the client opens the talk window
    /// for a Respond it did not ask for, UITalkManager::OnTalkPacket).
    /// </summary>
    public static void BeginTalk(GameSession session, FieldNpc npc) {
        // Counted first: a quest whose last condition is this talk shows its complete talk at once.
        session.ConditionUpdate(ConditionType.dialogue, codeLong: npc.Value.Id);
        session.ConditionUpdate(ConditionType.talk_in, codeLong: npc.Value.Id);

        int options = 0;
        var talkType = NpcTalkType.None;
        int shopId = EventNpcRules.ShopId(npc.EventShopId, npc.Value.Metadata.Basic.ShopId);
        if (shopId > 0) {
            // A beauty NPC's ShopId names its beauty shop; the client opens that one itself from the
            // NPC's script with Beauty 0 (UITalkManager::OpenTalkDialog), so there is no normal shop to load.
            if (!session.ServerTableMetadata.BeautyShopTable.Entries.ContainsKey(shopId)) {
                session.Shop.Load(shopId, npc.Value.Id);
            }
            talkType |= NpcTalkType.Dialog;
            options++;
        }

        session.ScriptMetadata.TryGet(npc.Value.Id, out ScriptMetadata? metadata);
        // The birthday card npc (kind 88, KR 11003587) has no Job state: its job condition (birthday) offers the card,
        // which the client opens only for a talk with no script (UITalkManager, OpenTalkUi); otherwise it talks its pick
        // script (11003587's script 10, whose npcScriptCondition birthday="!1" the parser does not read). A quest it
        // offers is still talked first.
        if (npc.Value.Metadata.Basic.Kind == 88 && options == 0 && session.Quest.GetAvailableQuests(npc.Value.Id).Count == 0
            && NpcTalkUtil.MeetsWindowJobCondition(session, npc.Value.Id)) {
            session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.Dialog, default));
            return;
        }
        ScriptState? scriptState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Script, metadata, npc.Value.Id, npc.MaidUid);
        ScriptState? selectState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Select, metadata, npc.Value.Id, npc.MaidUid);
        ScriptState? questState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Quest, metadata, npc.Value.Id);

        if (questState != null) {
            talkType |= NpcTalkType.Quest;
            options++;
        }

        if (scriptState?.Type == ScriptStateType.Job) {
            // Job script only counts as an additional option if quests are present.
            if (talkType.HasFlag(NpcTalkType.Quest)) {
                options++;
            }
            talkType |= NpcTalkType.Dialog;
        } else if (scriptState != null) {
            talkType |= NpcTalkType.Talk;
            options++;
        }

        if (options > 1 && selectState != null) {
            talkType |= NpcTalkType.Select;
        }

        switch (npc.Value.Metadata.Basic.Kind) {
            case >= 30 and < 40: // Beauty
            case 2: // Storage
            // With no pick script the client opens these itself (CNpc::GetTalkUiId): 86 the black market,
            // 88 the birthday card (BirthdayCardHandler).
            case 86: // BlackMarket
            case 88: // Birthday
            case 501:
                talkType |= NpcTalkType.Dialog;
                break;
            case >= 100 and <= 104: // Sky Fortress
            case >= 105 and <= 107: // Kritias
            case 108: // Humanitas
                talkType = NpcTalkType.Dialog;
                break;
        }

        // Determine which script to use.
        ScriptState? selectedState;
        if (npc.Value.Metadata.Basic.Kind is >= 100 and <= 108 || talkType.HasFlag(NpcTalkType.Select)) {
            selectedState = selectState;
        } else if (talkType.HasFlag(NpcTalkType.Quest)) {
            if (questState == null) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
            selectedState = questState;
            talkType = NpcTalkType.Quest;
            // now that quest is selected, change the metadata to the quest's metadata
            if (!session.ScriptMetadata.TryGet(session.Quest.GetAvailableQuests(npc.Value.Id).Keys.Min(), out metadata)) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
        } else if (scriptState == null && selectState == null) {
            if (talkType.HasFlag(NpcTalkType.Dialog)) {
                session.Send(NpcTalkPacket.Respond(npc, talkType, default));
                return;
            }
            session.Send(NpcTalkPacket.Close());
            npc.StopTalk();
            return;
        } else {
            selectedState = scriptState ?? selectState;
        }

        session.NpcScript = new NpcScriptManager(session, npc, metadata, selectedState, talkType);
        if (!session.NpcScript.BeginNpcTalk()) {
            session.NpcScript = null;
        }
    }

    private void HandleContinue(GameSession session, IByteReader packet) {
        session.NpcScript?.ProcessScriptFunction(false);
        // Not talking to an Npc.
        if (session.NpcScript == null) {
            return;
        }
        int pick = packet.ReadInt();

        /* The ordering is
        / Quests
        / Dialog
        / Talk */
        int addedOptions = 0;
        if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Select)) {
            // The client sends 0 or an index into its list (UITalkManager::SendNpcTalkMode2); a negative pick
            // passed the checks below and threw in the quest lookup. It ends the talk, as an unknown pick does on
            // a plain talk, so a resend cannot run the script's function again.
            if (pick < 0) {
                session.Send(NpcTalkPacket.Close());
                session.NpcScript.Npc?.StopTalk();
                session.NpcScript = null;
                return;
            }
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Quest)) {
                addedOptions += session.NpcScript.Quests.Count;
                // Picked quest
                if (pick < addedOptions) {
                    FieldNpc? npc = session.NpcScript.Npc;
                    if (npc == null) {
                        session.Send(NpcTalkPacket.Close());
                        return;
                    }
                    if (!session.ScriptMetadata.TryGet(session.NpcScript.Quests.ElementAt(pick).Value.Id, out ScriptMetadata? metadata)) {
                        session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
                        session.NpcScript = null;
                        return;
                    }
                    ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
                    session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);
                    if (!session.NpcScript.BeginQuest()) {
                        session.NpcScript = null;
                    }
                    return;
                }
            }

            NpcDialogue dialogue;
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Dialog)) {
                addedOptions++;
                if (pick < addedOptions) {
                    session.NpcScript.EnterDialog();
                    dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
                    session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
                    return;
                }
            }

            session.NpcScript.EnterTalk();
            dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
            session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
            return;
        }


        // Attempt to Continue, if |false|, the dialogue has terminated.
        if (!session.NpcScript.Continue(pick)) {
            session.NpcScript = null;
        }
    }

    private void HandleEnchant(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        int scriptId = packet.ReadInt();
        var eventType = packet.Read<ScriptEventType>();

        switch (eventType) {
            case ScriptEventType.EnchantSelect:
            case ScriptEventType.PeachySelect:
                if (!session.ScriptMetadata.TryGet(npcId, out ScriptMetadata? script) ||
                    !script.States.TryGetValue(Constants.EnchantMasterScriptID, out ScriptState? state)) {
                    return;
                }

                CinematicEventScript[] eventScripts = state.Contents.First().Events;
                session.NpcScript = new NpcScriptManager(session, eventScripts);
                return;
            // The merge dialog's result line request (UIItemMergeOptionDialog::RequestResultTalk,
            // uiitemmergeoptiondialog.cpp:1028, sent once the result is shown, in mode 6 or 4): the smith's line was
            // already sent after the result (ItemMergeManager), so nothing answers it in either mode.
            case ScriptEventType.EmpowerSelect:
                return;
            case ScriptEventType.EnchantComplete:
            case ScriptEventType.EnchantRetry:
                EnchantEvent(session, eventType);
                return;
        }
    }

    // NpcTalk 4, the enchant dialog opened by a field npc's talk (EnchantTalk). The item choice (1) needs nothing here:
    // RequestItemEnchant 1 stages the item and sends the npc's select line. Event 2 follows an enchant try
    // (RequestItemEnchant 4 and 6), which answers on its own; mode 6 ignores it too.
    private static void HandleEnchantWithNpc(GameSession session, IByteReader packet) {
        EnchantTalk.Event talkEvent = EnchantTalk.Read(packet);
        EnchantEvent(session, talkEvent.Type);
    }

    // The dialog's result (3: the npc's result line, then the staged item is cleared) and its retry button (4: the same
    // item staged again), for both talk modes.
    private static void EnchantEvent(GameSession session, ScriptEventType eventType) {
        switch (eventType) {
            case ScriptEventType.EnchantComplete:
                session.ItemEnchant.NpcTalkEvent(eventType);
                return;
            case ScriptEventType.EnchantRetry:
                session.ItemEnchant.Retry();
                return;
        }
    }

    private void HandleQuest(GameSession session, IByteReader packet) {
        if (session.NpcScript == null) {
            return;
        }

        int questId = packet.ReadInt();
        packet.ReadShort(); // 2 or 0. 2 = Start quest, 0 = Complete quest.

        FieldNpc? npc = session.NpcScript.Npc;
        if (npc == null) {
            session.Send(NpcTalkPacket.Close());
            return;
        }
        // Another quest's script is not this npc's to open: its talk runs the script's functions.
        if (!OpensAt(session, npc.Value.Id, questId)) {
            session.Send(NpcTalkPacket.Close());
            npc.StopTalk();
            session.NpcScript = null;
            return;
        }
        if (!session.ScriptMetadata.TryGet(questId, out ScriptMetadata? metadata)) {
            session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
            session.NpcScript = null;
            return;
        }

        ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
        session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);

        if (!session.NpcScript.BeginQuest()) {
            session.NpcScript = null;
        }
    }

    // The client sends NpcTalk 7 from the talk scene only: the quest it has just accepted at this npc
    // (CQuestAcceptCinematicComponent::SendQuestAccept, cinematiccomponent.cpp:587, short 2) or the following quest of
    // the one it has just completed here (CQuestCompleteCinematicComponent::vfunc_2, cinematiccomponent.cpp:648,
    // short 0). So the quest is one this npc offers now, one held and not done whose start or complete npc it is, or
    // one that can start and requires a quest completed at this npc.
    internal static bool OpensAt(GameSession session, int npcId, int questId) {
        if (session.Quest.TryGetQuest(questId, out Quest? held) && held.State != QuestState.Completed
            && (held.Metadata.Basic.StartNpc == npcId || held.Metadata.Basic.CompleteNpc == npcId)) {
            return true;
        }
        if (session.Quest.GetAvailableQuests(npcId).ContainsKey(questId)) {
            return true;
        }
        return session.QuestMetadata.TryGet(questId, out QuestMetadata? next) && session.Quest.CanStart(next)
            && next.Require.Quest.Any(id => session.Quest.TryGetQuest(id, out Quest? done) && done.State == QuestState.Completed
                && done.Metadata.Basic.CompleteNpc == npcId);
    }

    private void HandleAcceptAllianceQuest(GameSession session, IByteReader packet) {
        if (session.NpcScript == null) {
            return;
        }

        // UIAllianceQuestDialog onAccept (unit_140bf60f0_uialliancequestdialog.cpp:288): the quest id, then 2 when the
        // client's quest manager holds the quest as ready to complete, or 0 when it may be started. The window lists
        // the alliance quests itself from the client's quest table, so only alliance quests are taken here.
        int questId = packet.ReadInt();
        short mode = packet.ReadShort();
        HandleAllianceQuest(session, questId, mode);
    }

    private static void HandleAllianceQuest(GameSession session, int questId, short mode) {
        switch (mode) {
            case 0:
                if (!session.QuestMetadata.TryGet(questId, out QuestMetadata? metadata) || metadata.Basic.Type != QuestType.AllianceQuest) {
                    return;
                }
                // The quest's start talk, as the client asks for it (see BeginAllianceQuest); a quest without a
                // start script is started at once.
                if (session.NpcScript?.Npc is { } npc && session.ScriptMetadata.TryGet(questId, out ScriptMetadata? script)) {
                    var talk = new NpcScriptManager(session, npc, script, null, NpcTalkType.Quest);
                    if (talk.BeginAllianceQuest()) {
                        session.NpcScript = talk;
                        return;
                    }
                }
                QuestError error = session.Quest.Start(questId);
                if (error != QuestError.none) {
                    session.Send(QuestPacket.Error(error));
                }
                return;
            case 2:
                if (!session.Quest.TryGetQuest(questId, out Quest? quest) || quest.Metadata.Basic.Type != QuestType.AllianceQuest) {
                    return;
                }
                // Complete answers a full inventory itself.
                if (!session.Quest.CanComplete(quest)) {
                    session.Send(QuestPacket.Error(QuestError.s_quest_error_consume_fail));
                    return;
                }
                session.Quest.Complete(quest);
                return;
        }
    }

    private void HandleTalkAlliance(GameSession session) {
        if (session.NpcScript == null) {
            return;
        }
        session.NpcScript.EnterTalk();
        var dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
        session.Send(NpcTalkPacket.AllianceTalk(session.NpcScript.TalkType, dialogue));
    }
}
