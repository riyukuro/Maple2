using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Database.Extensions;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class NpcScriptManager {
    private readonly GameSession session;

    public readonly FieldNpc? Npc;
    private readonly ScriptMetadata? metadata;
    private readonly Dictionary<int, ScriptConditionMetadata>? scriptConditions;
    public SortedDictionary<int, QuestMetadata> Quests = new();
    public JobConditionMetadata? JobCondition;

    public NpcTalkType TalkType;
    public NpcTalkButton Button;

    // The enchant dialog's lines (the npc script's enchant master state). A talk with a field npc (NpcTalk 4) reads them
    // from that npc's script on its first enchant event.
    private CinematicEventScript[] eventScripts = [];

    public ScriptState? State { get; set; }
    public int Index { get; private set; } = 0;
    private readonly ILogger logger = Log.Logger.ForContext<NpcScriptManager>();

    public NpcScriptManager(GameSession session, FieldNpc? npc, ScriptMetadata? metadata, ScriptState? state, NpcTalkType talkType) {
        this.session = session;
        Npc = npc;
        this.metadata = metadata;
        TalkType = talkType;
        State = state;

        if (Npc != null) {
            if (session.ServerTableMetadata.ScriptConditionTable.Entries.TryGetValue(Npc.Value.Id, out Dictionary<int, ScriptConditionMetadata>? scriptConditionMetadata)) {
                scriptConditions = scriptConditionMetadata;
            }
            if (talkType.HasFlag(NpcTalkType.Quest)) {
                Quests = session.Quest.GetAvailableQuests(Npc.Value.Id);
            }

            if (state?.Type == ScriptStateType.Job) {
                JobCondition = session.ServerTableMetadata.JobConditionTable.Entries.GetValueOrDefault(Npc.Value.Id);
            }
        }
    }

    /// <summary>
    /// Used for Event Scripts
    /// </summary>
    public NpcScriptManager(GameSession session, CinematicEventScript[] eventScripts) {
        this.session = session;
        this.eventScripts = eventScripts;
    }

    public bool BeginNpcTalk() {
        if (State == null || Npc == null) {
            session.Send(NpcTalkPacket.Close());
            Npc?.StopTalk();
            return false;
        }
        var dialogue = new NpcDialogue(State.Id, 0, GetButton());

        if (TalkType.HasFlag(NpcTalkType.Quest)) {
            session.Send(QuestPacket.Talk(Npc, Quests.Values));
        }

        session.Send(NpcTalkPacket.Respond(Npc, TalkType, dialogue));
        Npc.Talk();
        TalkReached();
        ProcessScriptFunction();

        return true;
    }

    public bool BeginQuest() {
        TalkType = NpcTalkType.Quest;
        return QuestRespond();
    }

    // The start talk of an alliance mission accepted in the alliance window (NpcTalk 8, mode 0): the quest script's
    // first start state (100 to 199) whatever the quest's held state, as the window offers only quests that can start.
    // The reply is NpcTalk 8, which the client reads like Continue but shows in the talk scene (ReloadScript); the
    // talk's accept button sends Quest 2, which starts the quest.
    public bool BeginAllianceQuest() {
        TalkType = NpcTalkType.Quest;
        int stateId = metadata?.States.Keys.Where(id => id is >= 100 and < 200).DefaultIfEmpty().Min() ?? 0;
        if (stateId == 0 || !metadata!.States.TryGetValue(stateId, out ScriptState? state)) {
            return false;
        }
        State = state;
        Index = 0;
        Button = GetButton();

        var dialogue = new NpcDialogue(State.Id, Index, Button);
        session.Send(NpcTalkPacket.AllianceQuestTalk(TalkType, dialogue, metadata.Id));
        return true;
    }

    private bool QuestRespond() {
        State = GetQuestScriptState(metadata);
        if (State == null || State.Id == 0) {
            session.Send(NpcTalkPacket.Close());
            Npc?.StopTalk();
            return false;
        }
        Button = GetButton();

        var dialogue = new NpcDialogue(State.Id, Index, Button);
        session.Send(NpcTalkPacket.Continue(TalkType, dialogue, metadata!.Id));
        return true;
    }

    public void EnterDialog() {
        // Basic shops are treated slightly different
        if (Npc?.Value.Metadata.Basic.Kind is 1 or > 10 and < 20) {
            TalkType = NpcTalkType.Talk;
            State = null;
            Button = GetButton();
            return;
        }

        EnterTalk();
    }

    public void EnterTalk() {
        if (Npc == null) {
            return;
        }
        ScriptState? scriptState = NpcTalkUtil.GetInitialScriptType(session, ScriptStateType.Script, metadata, Npc.Value.Id, Npc.MaidUid);
        if (scriptState == null) {
            return;
        }
        if (scriptState.Type == ScriptStateType.Job) {
            JobCondition = session.ServerTableMetadata.JobConditionTable.Entries.GetValueOrDefault(Npc.Value.Id);
        } else {
            JobCondition = null;
        }

        TalkType = scriptState.Type == ScriptStateType.Script ? NpcTalkType.Talk : NpcTalkType.Dialog;

        State = scriptState;
        Button = GetButton();
        TalkReached();
    }

    // talk_in with a target: the target is the npc script the talk reaches (every KR row's target is a <script> id of
    // the npc in its code; 22200019 "Deliver item 30000181 to npc 11000327" is target 41, the script whose function
    // collects the item). Counted when the talk enters one of the npc's own script states; the talk's start counts
    // with target 0 (NpcTalkHandler.BeginTalk) for the rows without a target.
    private void TalkReached() {
        if (Npc == null || State == null || State.Type != ScriptStateType.Script || metadata?.Type != ScriptType.Npc) {
            return;
        }
        session.ConditionUpdate(ConditionType.talk_in, targetLong: State.Id, codeLong: Npc.Value.Id);
    }

    public bool Continue(int pick) {
        ScriptState? nextState = NextState(pick);
        if (nextState == null) {
            session.Send(NpcTalkPacket.Close());
            if (State?.Type == ScriptStateType.Job) {
                PerformJobScript();
            }
            Npc?.StopTalk();
            return false;
        }

        int questId = 0;
        if (metadata!.Type == ScriptType.Quest) {
            questId = metadata.Id;
        }
        if (nextState != State && !metadata.States.ContainsKey(nextState.Id)) {
            session.Send(NpcTalkPacket.Close());
            Npc?.StopTalk();
            return false;
        }

        if (nextState == State) {
            Index++;
        } else {
            Index = 0;
        }

        State = nextState;
        Button = GetButton();

        var dialogue = new NpcDialogue(State.Id, Index, Button);
        if (Index == 0) {
            TalkReached();
        }
        ProcessScriptFunction();
        session.Send(NpcTalkPacket.Continue(TalkType, dialogue, questId));
        return true;
    }

    private ScriptState? NextState(int pick) {
        if (State == null) {
            return null;
        }

        CinematicContent? content = State.Contents.ElementAtOrDefault(Index);
        if (content == null) {
            return null;
        }

        if (content.Distractors.Length > 0) {
            CinematicDistractor? distractor = content.Distractors.ElementAtOrDefault(pick);
            if (distractor == null) {
                return null;
            }

            IList<ScriptState> goToScripts = new List<ScriptState>();
            foreach (int goToScriptId in distractor.Goto) {
                if (!metadata!.States.TryGetValue(goToScriptId, out ScriptState? goToScript)) {
                    continue;
                }

                if (scriptConditions == null || !scriptConditions.TryGetValue(goToScript.Id, out ScriptConditionMetadata? scriptConditionMetadata)) {
                    goToScripts.Add(goToScript);
                    continue;
                }

                if (scriptConditionMetadata.ConditionCheck(session, Npc?.MaidUid ?? 0)) {
                    goToScripts.Add(goToScript);
                }
            }

            if (goToScripts.Count > 0) {
                return goToScripts[Random.Shared.Next(goToScripts.Count)];
            }

            IList<ScriptState> goToFailScripts = new List<ScriptState>();
            foreach (int goToFailScriptId in distractor.GotoFail) {
                if (!metadata!.States.TryGetValue(goToFailScriptId, out ScriptState? goToFailScript)) {
                    continue;
                }

                if (scriptConditions == null || !scriptConditions.TryGetValue(goToFailScript.Id, out ScriptConditionMetadata? scriptConditionMetadata)) {
                    goToFailScripts.Add(goToFailScript);
                    continue;
                }

                if (scriptConditionMetadata.ConditionCheck(session, Npc?.MaidUid ?? 0)) {
                    goToFailScripts.Add(goToFailScript);
                }
            }

            if (goToFailScripts.Count > 0) {
                return goToFailScripts[Random.Shared.Next(goToFailScripts.Count)];
            }
        }

        if (Index >= State.Contents.Length - 1) {
            return null;
        }

        return State;
    }

    private void PerformJobScript() {
        if (State?.Type != ScriptStateType.Job) {
            return;
        }

        if (JobCondition == null) {
            return;
        }

        if (JobCondition.Mesos > 0) {
            if (session.Currency.CanAddMeso(-JobCondition.Mesos) != -JobCondition.Mesos) {
                session.Send(ChatPacket.Alert(StringCode.s_err_lack_meso));
                return;
            }
            session.Currency.ChangeMeso(-JobCondition.Mesos);
        }

        if (JobCondition.MoveMapId > 0) {
            session.Send(session.PrepareField(JobCondition.MoveMapId, portalId: JobCondition.MovePortalId > 0 ? JobCondition.MovePortalId : -1)
                ? FieldEnterPacket.Request(session.Player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }
    }

    private ScriptState? GetQuestScriptState(ScriptMetadata? scriptMetadata) {
        return NpcTalkUtil.GetQuestScriptState(session, scriptMetadata, Npc?.Value.Id ?? 0);
    }

    private NpcTalkButton GetButton() {
        if (State == null) {
            return NpcTalkButton.None;
        }

        CinematicContent? content = State.Contents.ElementAtOrDefault(Index);
        if (content == null) {
            return NpcTalkButton.None;
        }
        if (content.ButtonType != NpcTalkButton.None) {
            return content.ButtonType;
        }

        if (content.Distractors.Length > 0) {
            return NpcTalkButton.SelectableDistractor;
        }

        if (Index < State.Contents.Length - 1) {
            return NpcTalkButton.Next;
        }

        if (TalkType.HasFlag(NpcTalkType.Select)) {
            if (State.Contents.Length > 0) {
                return NpcTalkButton.SelectableTalk;
            }
            return NpcTalkButton.None;
        }

        switch (State.Type) {
            case ScriptStateType.Job:
                switch (Npc?.Value.Metadata.Basic.Kind) {
                    case >= 30 and < 40: // Beauty
                        return NpcTalkButton.SelectableBeauty;
                    case 80:
                        return NpcTalkButton.ChangeJob;
                    case 81:
                        return NpcTalkButton.PenaltyResolve;
                    case 82:
                        return NpcTalkButton.TakeBoat;
                    case 501:
                        return NpcTalkButton.Roulette;
                }
                break;
            case ScriptStateType.Select:
                switch (Npc?.Value.Metadata.Basic.Kind) {
                    case 1 or > 10 and < 20: // Shop
                        return NpcTalkButton.None;
                    case 2: // Storage
                        return NpcTalkButton.None;
                }
                return NpcTalkButton.SelectableTalk;
            case ScriptStateType.Quest:
                switch (State.Id / 100) {
                    case 1:
                        return NpcTalkButton.QuestAccept;
                    case 2:
                        return NpcTalkButton.QuestProgress;
                    case 3:
                        return NpcTalkButton.QuestComplete;
                }
                break;
        }

        return NpcTalkButton.Close;
    }

    public void ProcessScriptFunction(bool enter = true) {
        if (State == null || session.Field is null) {
            return;
        }

        if (!session.ServerTableMetadata.ScriptFunctionTable.Entries.TryGetValue(metadata!.Id, out Dictionary<int, Dictionary<int, ScriptFunctionMetadata>>? scriptFunctions) ||
            !scriptFunctions.TryGetValue(State.Id, out Dictionary<int, ScriptFunctionMetadata>? functions) ||
            !functions.TryGetValue(State.Contents.ElementAt(Index).FunctionId, out ScriptFunctionMetadata? scriptFunction)) {
            return;
        }

        if ((scriptFunction.EndFunction & enter) ||
            (!scriptFunction.EndFunction & !enter)) {
            return;
        }

        // A divorce step (attribute divorce). When it is refused the client is sent the reason and the rest of
        // the function is not run.
        if (!string.IsNullOrEmpty(scriptFunction.Divorce) && !session.Marriage.DivorceFunction(scriptFunction.Divorce)) {
            return;
        }

        // NpcTalk packets have to be sent first before any movement, items, etc
        if (scriptFunction.PortalId > 0) {
            session.Send(NpcTalkPacket.MovePlayer(scriptFunction.PortalId));
        }

        if (!string.IsNullOrEmpty(scriptFunction.UiName)) {
            session.Send(NpcTalkPacket.OpenDialog(scriptFunction.UiName, scriptFunction.UiArg));
        }

        if (!string.IsNullOrEmpty(scriptFunction.MoveMapMovie)) {
            session.Send(NpcTalkPacket.Cutscene(scriptFunction.MoveMapMovie, scriptFunction.MoveMapId));
        }

        if (scriptFunction.PortalId > 0 && session.Field.TryGetPortal(scriptFunction.PortalId, out FieldPortal? dstPortal)) {
            session.Send(PortalPacket.MoveByPortal(session.Player, dstPortal));
        }

        if (scriptFunction.CollectItems.Count > 0) {
            if (!session.Item.Inventory.ConsumeItemComponents(scriptFunction.CollectItems.ToList())) {
                logger.Error("Failed to consume items for script function {FunctionId} on npc {NpcId}", scriptFunction.FunctionId, Npc?.Value.Id);
            }
        }

        if (!string.IsNullOrEmpty(scriptFunction.SetTriggerValueKey)) {
            if (int.TryParse(scriptFunction.SetTriggerValue, out int value)) {
                session.Field.UserValues.Set(scriptFunction.SetTriggerValueTriggerId, scriptFunction.SetTriggerValueKey, value);
            }
        }

        if (scriptFunction.CollectMeso > 0) {
            session.Currency.ChangeMeso(-scriptFunction.CollectMeso);
        }

        IList<Item> items = new List<Item>();
        foreach (ItemComponent item in scriptFunction.PresentItems) {
            Item? newItem = session.Field?.ItemDrop.CreateItem(item.ItemId, item.Rarity, item.Amount);
            if (newItem == null) {
                continue;
            }
            // The mesos are already taken: what the bag (or furnishing storage) cannot take goes by mail.
            if (!session.Item.AddOrMail(newItem, true)) {
                logger.Error("Script reward {ItemId} x{Amount} of {CharacterId} not given", item.ItemId, item.Amount, session.CharacterId);
                continue;
            }
            items.Add(newItem);
        }

        if (items.Count > 0) {
            session.Send(NpcTalkPacket.RewardItem(items));
        }

        if (Npc is { MaidUid: not 0 } && (scriptFunction.MaidMoodIncrease != 0 || scriptFunction.MaidClosenessIncrease != 0 || scriptFunction.MaidPay)) {
            MaidFunction(scriptFunction, Npc.MaidUid);
        }

        if (scriptFunction.PresentExp > 0) {
            session.Player.Value.Character.Exp += scriptFunction.PresentExp;
            session.Send(NpcTalkPacket.RewardExp(scriptFunction.PresentExp));
        }

        if (scriptFunction.MoveMapId > 0) {
            session.Send(session.PrepareField(scriptFunction.MoveMapId, portalId: scriptFunction.MovePortalId > 0 ? scriptFunction.MovePortalId : -1)
                ? FieldEnterPacket.Request(session.Player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }
    }

    // The maid functions of a maid npc's script (maidMoodUp, maidAffinityUp, maidPay), for its owner only: the talk
    // menus that run them are behind maid_auth. maidPay takes the salary of table/maidsalary.xml (mesos for salary
    // type 0, else merets) and extends the contract by PeriodOfMaidEmployment days from its end, or from now when it
    // has ended. All of it runs on the field thread, which owns the maid: the salary is taken there, after the maid is
    // found still placed, so no salary is taken for a maid removed meanwhile.
    private void MaidFunction(ScriptFunctionMetadata scriptFunction, long maidUid) {
        FieldManager? field = session.Field;
        Maid? maid = field?.GetMaid(maidUid);
        if (field == null || maid == null || maid.OwnerId != session.AccountId) {
            return;
        }
        MaidTable table = session.TableMetadata.MaidTable;
        ConstantsTable constants = session.ServerTableMetadata.ConstantsTable;
        MaidTable.Entry? salary = null;
        if (scriptFunction.MaidPay && !table.Entries.TryGetValue(maid.MaidId, out salary)) {
            logger.Warning("No salary for maid {MaidId}", maid.MaidId);
        }
        int mood = scriptFunction.MaidMoodIncrease;
        int exp = scriptFunction.MaidClosenessIncrease;
        IReadOnlyDictionary<int, int> exps = table.Exps ?? new Dictionary<int, int>();
        field.Scheduler.Schedule(() => {
            if (!MaidRules.StillPlaced(field.GetMaid(maidUid), maid) || maid.OwnerId != session.AccountId) {
                return; // removed meanwhile: nothing was taken
            }
            bool changed = mood != 0 && MaidRules.AddMood(maid, mood);
            if (exp != 0) {
                int level = maid.Level, oldExp = maid.Exp;
                MaidRules.AddExp(maid, exp, exps, constants.MaidAffinityMax);
                changed |= maid.Level != level || maid.Exp != oldExp;
                if (maid.Level > level) {
                    session.ConditionUpdate(ConditionType.maid_affinity, counter: maid.Level); // 23500014, the level reached
                }
            }
            if (salary != null) {
                changed |= MaidRules.Pay(maid, () => TakeSalary(salary), DateTime.UtcNow.ToEpochSeconds(), constants.PeriodOfMaidEmployment);
            }
            if (changed) {
                field.SaveMaid(maid);
            }
        });
    }

    // The salary of a maid row: mesos for salary type 0, else merets; false when the talker cannot pay it.
    private bool TakeSalary(MaidTable.Entry salary) {
        if (session.State is SessionState.Disconnected) {
            return false;
        }
        if (salary.SalaryType == 0) {
            if (session.Currency.Meso < salary.Salary) {
                return false;
            }
            session.Currency.ChangeMeso(-salary.Salary);
            return true;
        }
        if (session.Currency.Meret < salary.Salary) {
            return false;
        }
        session.Currency.ChangeMeret(-salary.Salary);
        return true;
    }

    #region EnchantTalk
    public void EnchantResultScript(ScriptEventType eventType, EnchantResult result = EnchantResult.None, ItemEnchantError error = ItemEnchantError.s_itemenchant_unknown_err, int enchantLevel = 0, short rarity = 0, int failCount = 0) {
        int eventId = 0;
        if (eventScripts.Length == 0 && Npc != null) {
            eventScripts = EnchantEvents(session, Npc.Value.Id);
        }

        if (!session.ServerTableMetadata.ScriptEventConditionTable.Entries.TryGetValue(eventType, out Dictionary<int, ScriptEventConditionMetadata>? scriptDict)) {
            return;
        }

        // A row with a level or grade is the line for that case, a row without is the default line of its result
        // (scriptEventCondition 2100 "success default", 2300 "fail at +1-2" before 2203 "fail default"). Chosen here:
        // the rows that name a level or a grade are tried first, each group in id order.
        foreach (ScriptEventConditionMetadata value in scriptDict.Values
                     .OrderBy(row => row.EnchantLevel.Length == 0 && row.Rarity == 0)
                     .ThenBy(row => row.Id)) {
            if (EventConditionCheck(value, enchantLevel, rarity, failCount, result, error)) {
                eventId = value.Id;
                break;
            }
        }

        RunEventScript(eventId);
    }

    private bool EventConditionCheck(ScriptEventConditionMetadata scriptMetadata, int enchantLevel, short rarity, int failCount, EnchantResult result, ItemEnchantError error) {
        if (error != ItemEnchantError.s_itemenchant_unknown_err && scriptMetadata.ErrorCode != error) {
            return false;
        }

        if (scriptMetadata.Rarity > 0 && scriptMetadata.Rarity != rarity) {
            return false;
        }

        // No enchantLevel in the row (the ingest leaves the list empty): any level.
        if (scriptMetadata.EnchantLevel.Length > 0 && !scriptMetadata.EnchantLevel.Contains(enchantLevel)) {
            return false;
        }

        if (scriptMetadata.FailCount > 0 || scriptMetadata.DamageType != EnchantDamageType.None) {
            // Returning false here because we don't have the fail count or damage type
            return false;
        }

        if (scriptMetadata.ResultType != EnchantResult.None && scriptMetadata.ResultType != result) {
            return false;
        }

        return true;
    }

    // The lines of an npc's enchant master state (ConstantsTable.EnchantMasterScriptID, 31): the dialog opened from a
    // talk with a field npc sends its events in NpcTalk 4 (UIItemModifyDialogBase::vfunc_85), and the client shows the
    // reply (NpcTalk 4 Update) in the open talk (UITalkManager::OnTalkPacket mode 4) as for NpcTalk 6.
    public static CinematicEventScript[] EnchantEvents(GameSession session, int npcId) {
        if (!session.ScriptMetadata.TryGet(npcId, out ScriptMetadata? script) ||
            !script.States.TryGetValue(session.ServerTableMetadata.ConstantsTable.EnchantMasterScriptID, out ScriptState? state)) {
            return [];
        }
        return state.Contents.FirstOrDefault()?.Events ?? [];
    }

    private void RunEventScript(int eventId) {
        if (eventId == 0) {
            return;
        }
        CinematicEventScript? script = eventScripts.FirstOrDefault(eventScript => eventScript.Id == eventId);
        if (script == null) {
            return;
        }
        ScriptContent content = script.Contents[Random.Shared.Next(script.Contents.Length)];
        session.Send(NpcTalkPacket.Update(content));
    }
    #endregion
}
