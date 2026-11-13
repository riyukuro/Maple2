using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Serilog;
using static Maple2.Model.Metadata.AiMetadata;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class AiState {
    protected readonly ILogger Logger = Log.ForContext<AiState>();
    private readonly FieldNpc actor;
    public AiMetadata? AiMetadata { get; private set; }
    private Node? battle;
    private Node? battleEnd;

    private List<StackEntry> aiStack = new List<StackEntry>();
    private NpcTask? currentTask = null;
    private long currentLimitTick = 0;

    private AiMetadata? lastEvaluated;
    private DecisionTreeType currentTree;
    // The field tick the battle time counts from (combatTime; setData aiBattleTime moves it).
    private long battleStartTick;

    private Dictionary<string, long> coolTimes = new();
    private List<Node> reservedNodes = new List<Node>();

    private enum DecisionTreeType {
        None,
        Battle,
        BattleEnd,
    }

    public AiState(FieldNpc actor, string aiPath) {
        this.actor = actor;
        if (string.IsNullOrEmpty(aiPath)) {
            return;
        }

        if (aiPath == "AI_DefaultNew.xml") {
            aiPath = Constant.DefaultAiPath;
        }

        string? resolved = AiPathRules.Resolve(aiPath, name => actor.Field.AiMetadata.TryGet(name, out _));
        if (resolved != null && resolved != aiPath && resolved == AiPathRules.NormalFolder(aiPath)) {
            Logger.Warning("{AiPath} could not be found for {ValueId}. using {Resolved}", aiPath, actor.Value.Id, resolved);
        }
        aiPath = resolved ?? aiPath;
        if (!actor.Field.AiMetadata.TryGet(aiPath, out AiMetadata? metadata)) {
            Logger.Error("{AiPath} could not be found for {ValueId}. setting as default AI", aiPath, actor.Value.Id);
            if (!actor.Field.AiMetadata.TryGet(Constant.DefaultAiPath, out metadata)) {
                throw new KeyNotFoundException($"Could not find default AI {Constant.DefaultAiPath} for {actor.Value.Id}");
            }
        }

        AiMetadata = metadata;

        if (AiMetadata.Battle.Length != 0) {
            battle = new Node("battle", AiMetadata.Battle);
        }

        if (AiMetadata.BattleEnd.Length != 0) {
            battleEnd = new Node("battle", AiMetadata.BattleEnd);
        }

        if (AiMetadata.Reserved.Length != 0) {
            reservedNodes = AiMetadata.Reserved.Cast<Node>().ToList();
        }
    }

    public bool IsProcessingNodes() {
        return aiStack.Count > 0;
    }

    // A task started from outside the tree (the GM mob-attack cast) holds the AI until it ends. Otherwise the next
    // Update runs a node whose own cast, at the same BattleAction priority, overrides and cancels it.
    public void Hold(NpcTask task) {
        if (currentTask != task) {
            currentTask?.Cancel();
        }
        currentTask = task;
        currentLimitTick = 0;
    }

    public bool IsWaitingOnTask(long tickCount) {
        if (currentTask is null) {
            return false;
        }

        if (currentLimitTick != 0 && tickCount >= currentLimitTick) {
            return false;
        }

        return !currentTask.IsDone;
    }

    public void Update(long tickCount) {
        // A chase ends with its target: the npc does not walk on to an actor it no longer fights (a dead player).
        bool chaseOver = currentTask is MovementState.NpcMoveTargetDistanceTask chase
            && NpcTargetRules.ChaseEnds(chase.Target.ObjectId, actor.BattleState.TargetId);
        if (!chaseOver && IsWaitingOnTask(tickCount)) {
            return;
        }

        currentTask?.Cancel();

        currentTask = null;
        currentLimitTick = 0;

        if (actor.IsDead) {
            return;
        }

        if (AiMetadata is null) {
            if (actor.Value.Metadata.AiPath != "") {
                actor.AppendDebugMessage("Missing AI\n");
                actor.AppendDebugMessage(actor.Value.Metadata.AiPath + "\n");
            }

            aiStack.Clear();
            currentTree = DecisionTreeType.None;

            return;
        }

        bool isInBattle = actor.BattleState.InBattle;

        if (!isInBattle) {
            // A battle that ends with the npc alive runs the battleEnd tree, unless it is onlyDead (then it runs
            // at the death, RunDeathBattleEnd). The tree was set here and then never pushed.
            if (currentTree == DecisionTreeType.Battle) {
                aiStack.Clear();

                if (battleEnd is not null && !AiMetadata.BattleEndOnlyDead) {
                    currentTree = DecisionTreeType.BattleEnd;
                    Push(battleEnd);
                } else {
                    currentTree = DecisionTreeType.None;
                }
            } else if (currentTree == DecisionTreeType.BattleEnd && aiStack.Count == 0) {
                currentTree = DecisionTreeType.None;
            }

            if (currentTree == DecisionTreeType.BattleEnd) {
                RunStack(tickCount);
            }
            return;
        } else if (currentTree == DecisionTreeType.BattleEnd) {
            aiStack.Clear();

            currentTree = DecisionTreeType.None;
        }

        if (isInBattle && battle is not null) {
            if (currentTree != DecisionTreeType.Battle) {
                battleStartTick = actor.Field.FieldTick;
            }
            currentTree = DecisionTreeType.Battle;
        }

        if (lastEvaluated != AiMetadata) {
            aiStack.Clear();
        }

        lastEvaluated = AiMetadata;

        foreach (var node in reservedNodes.ToList()) {
            if (ProcessCondition((dynamic) node)) {
                aiStack.Clear();
                reservedNodes.Remove(node);
                Push(node);
            }
        }

        if (aiStack.Count == 0) {
            if (currentTree == DecisionTreeType.Battle) {
                Push(battle!);
            } else if (currentTree == DecisionTreeType.BattleEnd) {
                Push(battleEnd!);
            }
        }

        RunStack(tickCount);
    }

    // Both battleEnd kinds run at the death: onlyDead="true" only then, a plain one also when a battle ends
    // (the AI file's comment says onlyDead makes battleEnd run only at death). The npc stops, so the tree
    // runs through at once: a node that starts a task (a move, a skill) does not wait, and the task is cancelled.
    public void RunDeathBattleEnd() {
        currentTask?.Cancel();
        currentTask = null;
        currentLimitTick = 0;
        aiStack.Clear();
        if (battleEnd is null) {
            currentTree = DecisionTreeType.None;
            return;
        }

        currentTree = DecisionTreeType.BattleEnd;
        Push(battleEnd);
        // A locked or looping tree must not hold the field thread.
        for (int steps = 0; aiStack.Count > 0 && steps < MaxDeathSteps; steps++) {
            if (!Step()) {
                continue;
            }
            currentTask?.Cancel();
            currentTask = null;
            currentLimitTick = 0;
        }

        aiStack.Clear();
        currentTree = DecisionTreeType.None;
    }

    private const int MaxDeathSteps = 1000;

    private void RunStack(long tickCount) {
        while (aiStack.Count > 0) {
            if (Step() && IsWaitingOnTask(tickCount)) {
                break;
            }
        }
    }

    // Runs the next entry of the top of the stack; false when that node was done and popped.
    private bool Step() {
        StackEntry entry = aiStack.Last();

        int index = entry.Index;
        int last = aiStack.Count - 1;

        if (index >= entry.Node.Entries.Length) {
            aiStack.RemoveAt(last);

            return false;
        }

        int nextIndex = entry.LockIndex ? entry.Node.Entries.Length : index + 1;

        aiStack[last] = new StackEntry() {
            Node = entry.Node,
            Index = nextIndex,
            LockIndex = entry.LockIndex,
        };

        Process(entry.Node.Entries[index]);
        return true;
    }

    private void SetNodeTask(NpcTask? task, long limit = 0) {
        currentTask = task;
        currentLimitTick = limit == 0 ? 0 : actor.Field.FieldTick + limit;
    }

    private struct StackEntry {
        public Node Node;
        public int Index;
        public bool LockIndex;
    }

    private void Push(Node node) {
        aiStack.Add(new StackEntry() {
            Node = node,
        });
    }

    private void Push(AiPreset aiPreset) {
        actor.AppendDebugMessage($"> Preset: '{aiPreset.Name}'\n");

        AiPresetDefinition? definition = AiMetadata?.AiPresets.First(value => value.Name == aiPreset.Name);

        if (definition is null) {
            return;
        }

        Push(definition);
    }

    // An AI script that names a skill index the npc lacks hits it on every tick; the warning is logged once per npc,
    // script and index.
    private static readonly ConcurrentDictionary<(int NpcId, string Script, int Idx), byte> MissingSkillsLogged = new();

    public static bool FirstMissingSkill(int npcId, string script, int idx) => MissingSkillsLogged.TryAdd((npcId, script, idx), 0);

    private void WarnMissingSkill(int idx) {
        string script = AiMetadata?.Name ?? "";
        if (!FirstMissingSkill(actor.Value.Id, script, idx)) {
            return;
        }

        Logger.Warning("Missing skillIdx {Idx} in {NpcName}[{NpcId}] script '{Script}' (logged once per npc, script and index). Xml.m2d/m2h might be out of date. Check the MS2 Hub resources!",
            idx, actor.Value.Metadata.Name, actor.Value.Id, script);
    }

    private SkillMetadata? TryGetSkill(int idx, short levelOverride = 0) {
        // idx starts at 1
        if (idx < 1 || idx > actor.Skills.Length) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Invalid Skill Idx {idx}\n");

            WarnMissingSkill(idx);

            return null;
        }

        SkillMetadata? skill = actor.Skills[idx - 1];

        if (skill is not null && levelOverride != 0) {
            actor.Field.SkillMetadata.TryGet(skill.Id, levelOverride, out skill);
        }

        if (skill is null) {
            actor.AppendDebugMessage($"{actor.Value.Metadata.Name}[{actor.Value.Id}]\n");
            actor.AppendDebugMessage($"{AiMetadata!.Name}\n");
            actor.AppendDebugMessage($"Missing Skill Idx {idx}\n");

            WarnMissingSkill(idx);
        }

        return skill;
    }

    private void Push(Entry entry) {
        Push((dynamic) entry);
    }

    private bool CanEvaluateNode(Entry entry) {
        if (entry is not Node node) {
            return true;
        }

        bool hasTicked = coolTimes.TryGetValue(GetNodeKey(entry), out long lastTick);

        Type parent = entry.GetType();
        long? initialCooltime = parent.GetProperty("InitialCooltime")?.GetValue(entry, null) as long?;
        if (initialCooltime.HasValue && !hasTicked) {
            if (initialCooltime.Value > 0) {
                coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick + initialCooltime.Value;
                return false;
            }
        }

        if (!hasTicked) {
            coolTimes[GetNodeKey(entry)] = 0;
            return true;
        }


        long? cooltime = parent.GetProperty("Cooltime")?.GetValue(entry, null) as long?;

        if (cooltime.HasValue) {
            return lastTick + cooltime.Value < actor.Field.FieldTick;
        }

        return lastTick < actor.Field.FieldTick;
    }

    private bool PerformOperation(AiConditionOp op, int value, int currentValue) {
        switch (op) {
            case AiConditionOp.Equal:
                return value == currentValue;
            case AiConditionOp.Greater:
                return value < currentValue;
            case AiConditionOp.Less:
                return value > currentValue;
            case AiConditionOp.GreaterEqual:
                return value <= currentValue;
            case AiConditionOp.LessEqual:
                return value >= currentValue;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }
    private string GetNodeKey(Entry entry) {
        return $"{entry.Name}_{entry.GetHashCode()}";
    }

    private void Process(Entry entry) {
        if (entry is Node node) {
            actor.AppendDebugMessage($"> Node: {entry.Name}\n");

            ProcessNode((dynamic) entry);

            if (node.Entries.Length > 0 && aiStack.Last().Node is not SelectNode && aiStack.Last().Node != node && CanEvaluateNode(node.Entries[0])) {
                Push(node);
            }

            coolTimes[GetNodeKey(entry)] = actor.Field.FieldTick;
            return;
        }

        if (entry is AiPreset) {
            actor.AppendDebugMessage($"> AiPreset: {entry.Name}\n");
            Push(entry);
        }
    }

    private void ProcessNode(TraceNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? _)) {
            return;
        }

        float distance = 0;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            // Walk until the target is in the skill's reach, as the client reads it (AiSkillReach).
            distance = AiSkillReach.Radius(skill.Data);
        }

        float speed = 1;

        if (node.Speed != 0) {
            speed = node.Speed;
        }

        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SkillNode node) {
        SkillMetadata? skill = TryGetSkill(node.Idx);

        if (skill is null) {
            return;
        }

        NpcTask task = actor.CastAiSkill(skill.Id, skill.Level, node.FaceTarget, node.FacePos);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(TeleportNode node) {
        actor.Position = node.Pos;

        if (node.FacePos == new Vector3(0, 0, 0)) {
            return;
        }

        Vector3 offset = node.FacePos - actor.Position;
        float squareDistance = offset.LengthSquared();

        if (MathF.Abs(squareDistance) < 1e-5f) {
            return;
        }

        actor.Transform.LookTo((1 / MathF.Sqrt(squareDistance)) * offset);
    }

    private void ProcessNode(StandbyNode node) {
        IActor? faceTarget = AiStandbyFace.FacesTarget(node.FaceTarget) ? actor.BattleState.Target : null;
        NpcTask task = actor.MovementState.TryStandby(faceTarget, false);

        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SetDataNode node) {
        actor.AiExtraData[node.Key] = node.Value;
        if (node.Key == AiCombatTime.BattleTimeKey) {
            battleStartTick = actor.Field.FieldTick - node.Value;
        }
    }

    private void ProcessNode(TargetNode node) {
        actor.BattleState.TargetNode = node;
    }

    private void ProcessNode(SayNode node) {
        actor.Field.Broadcast(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.NpcAi, actor.ObjectId, node.Message, node.DurationTick, node.DelayTick));

        actor.AppendDebugMessage($"Say '{node.Message}'\n");
    }

    private void ProcessNode(SetValueNode node) {
        // Assumed that we increment the current by the value if isModify is true
        if (node.IsModify) {
            if (actor.AiExtraData.TryGetValue(node.Key, out int oldValue)) {
                actor.AiExtraData[node.Key] = oldValue + node.Value;
                return;
            }
        }
        actor.AiExtraData[node.Key] = node.Value;
    }

    private void ProcessNode(ConditionsNode node) {
        Condition? passed = null;

        foreach (Condition condition in node.Conditions) {
            if (ProcessCondition((dynamic) condition)) {
                passed = condition;

                break;
            }
        }

        if (passed is null) {
            actor.AppendDebugMessage("Failed conditions\n");

            return;
        }

        actor.AppendDebugMessage($"+ Condition: '{passed.Name}'\n");

        Push(passed);
    }

    // The client flies the npc (see AiJump), and the server moves its own position along the same arc over the same
    // time (MovementState.StartJump). The next node (the in-air skill in the files, the landing skill after it) runs
    // during the arc, as the files' order has it.
    private void ProcessNode(JumpNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
        Vector3 from = actor.Position;
        if (actor.Navigation == null || !actor.Navigation.UpdatePosition(node.Pos)) {
            actor.Position = node.Pos;
        }
        Vector3 to = actor.Position;
        float duration = AiJump.Duration(from, to, node.Speed);
        actor.MovementState.StartJump(from, to, duration, node.HeightMultiplier);
        actor.SequenceCounter++;
        actor.Field.Broadcast(NpcControlPacket.Jump(actor, AiJump.Sequence(node.Type), from, to, duration, node.HeightMultiplier));
    }

    private void ProcessNode(SelectNode node) {
        var weightedEntries = new WeightedSet<(Entry, int)>();

        int[] npcProbs = actor.Value.Metadata.Skill.Entries.Select(skill => skill.Prob).ToArray();
        int[] weights = AiSelect.Weights(node.Prob, node.useNpcProb, npcProbs, node.Entries.Length);
        int count = node.useNpcProb ? weights.Length : Math.Min(node.Prob.Length, weights.Length);
        for (int i = 0; i < count; ++i) {
            // Under useNpcProb a weight of 0 is a branch for a skill the npc does not have (or does not use).
            if ((node.useNpcProb && weights[i] <= 0) || !CanEvaluateNode(node.Entries[i])) {
                continue;
            }

            weightedEntries.Add((node.Entries[i], i), weights[i]);
        }

        if (weightedEntries.Count == 0) {
            return;
        }

        (Entry entry, int index) selected = weightedEntries.Get();

        aiStack[aiStack.Count - 1] = new StackEntry {
            Node = node,
            Index = selected.index,
            LockIndex = true,
        };

        Push(selected.entry);
        Process(selected.entry);
    }

    private void ProcessNode(MoveNode node) {
        NpcTask task = actor.MovementState.TryMoveTo(node.Destination, true, node.Animation, node.Speed);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(SummonNode node) {
        int? npcId = AiSummon.NpcId(actor.Value.Metadata.Basic.SummonNpcIds, node.NpcId);
        if (npcId == null) {
            Logger.Warning("Summon npcId {Index} of npc {NpcId} is not in its summonNpcIDs", node.NpcId, actor.Value.Id);
            return;
        }
        if (!actor.Field.NpcMetadata.TryGet(npcId.Value, out NpcMetadata? npc)) {
            Logger.Warning("Summon of npc {NpcId}: no npc {SummonId}", actor.Value.Id, npcId.Value);
            return;
        }

        TimeSpan delay = AiSummon.Delay(node.DelayTick);
        if (delay == TimeSpan.Zero) {
            actor.Summon(npc, node);
        } else {
            actor.Field.Scheduler.Schedule(() => actor.Summon(npc, node), delay);
        }
    }

    private void ProcessNode(TriggerSetUserValueNode node) {
        actor.Field.UserValues.Set(node.TriggerID, node.Key, node.Value);
    }

    private void ProcessNode(RideNode node) {
        actor.RideNpcIds = AiRide.Riding(node.IsRideOff, node.RideNpcIDs);
    }

    private void ProcessNode(SetSlaveValueNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
        foreach (FieldNpc slave in AiSummon.ValueTargets(actor.Slaves(), node.IsRandom, Random.Shared)) {
            bool hasOld = slave.AiExtraData.TryGetValue(node.Key, out int oldValue);
            slave.AiExtraData[node.Key] = AiSummon.ModifiedValue(hasOld, oldValue, node.Value, node.IsModify);
        }
    }

    // A slave sets the key on the npc that summoned it (in the summon scans no master="None" summon's AI has this
    // node); an npc that is no one's slave sets nothing.
    private void ProcessNode(SetMasterValueNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
        if (actor.Master is not { IsDead: false } master) {
            return;
        }
        bool hasOld = master.AiExtraData.TryGetValue(node.Key, out int oldValue);
        master.AiExtraData[node.Key] = AiSummon.ModifiedValue(hasOld, oldValue, node.Value, node.IsModify);
    }

    private void ProcessNode(RunawayNode node) {
        if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out IActor? target)) {
            return;
        }

        float? skillDistance = null;

        if (node.SkillIdx != 0) {
            SkillMetadata? skill = TryGetSkill(node.SkillIdx);

            if (skill is null) {
                return;
            }

            skillDistance = AiSkillReach.Radius(skill.Data);
        }

        // till="1200" is the distance to keep; unbounded (float.MaxValue) made PathAwayFrom aim ~2^31 away.
        float distance = AiRunaway.Distance(node.Till, skillDistance);
        NpcTask task = actor.MovementState.TryMoveTargetDistance(actor.BattleState.Target!, distance, true, node.Animation, 1);

        SetNodeTask(task, node.Limit);
    }

    private void ProcessNode(MinimumHpNode node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];
        float currentHpPercent = ((float) health.Current / (float) health.Total) * 100f;

        if (currentHpPercent > node.HpPercent) {
            return;
        }

        // Heal back to node.HpPercent
        long newHp = (long) ((node.HpPercent / 100) * health.Total);
        actor.Stats.Values[BasicAttribute.Health].Current = Math.Clamp(newHp, 0, health.Total);
    }

    private void ProcessNode(BuffNode node) {
        IActor target = actor;

        if (node.IsTarget) {
            IActor? newTarget = null;

            if (!actor.Field.TryGetActor(actor.BattleState.TargetId, out newTarget)) {
                return;
            }

            target = newTarget;
        }

        if (node.Type == NodeBuffType.Remove) {
            target.Buffs.Remove(node.Id, actor.ObjectId);

            return;
        }

        target.Buffs.AddBuff(actor, actor, node.Id, BuffNodeLevel(node.Level), actor.Field.FieldTick);
    }

    // Most AI buff nodes (859 of 1620) carry no level attribute and parse as level 0, which no additional effect has;
    // a missing level is the effect's first level.
    public static short BuffNodeLevel(short level) => level > 0 ? level : (short) 1;

    private void ProcessNode(TargetEffectNode node) {
        actor.Field.Broadcast(NpcNoticePacket.TargetEffect(actor.BattleState.TargetId, node.EffectName));
    }

    // The group's hidden vibrate objects fade in and drop onto their place (AiVibrate).
    private void ProcessNode(ShowVibrateNode node) {
        foreach (string vibrateId in actor.Field.Vibrate.Show(node.GroupId)) {
            actor.Field.Broadcast(VibratePacket.Show(vibrateId));
        }
    }

    private void ProcessNode(SidePopupNode node) {
        actor.Field.Broadcast(NpcNoticePacket.SidePopup(node.Type, node.Duration, node.Illust, node.Voice, node.Script, node.Sound));
    }

    private void ProcessNode(SetValueRangeTargetNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }

        int range = node.Radius;
        int height = node.Height;

        if (range == 0) {
            range = 10;
        }

        if (height == 0) {
            height = 10;
        }

        foreach (FieldNpc npc in actor.Field.EnumerateNpcs()) {
            if (Vector2.Distance(new Vector2(npc.Position.X, npc.Position.Y), new Vector2(actor.Position.X, actor.Position.Y)) > range) {
                continue;
            }

            if (npc.Position.Z < actor.Position.Z - height) {
                continue;
            }

            if (npc.Position.Z > actor.Position.Z + height) {
                continue;
            }

            // The key goes on each npc in range, not on the npc that runs the node.
            bool hasOld = npc.AiExtraData.TryGetValue(node.Key, out int oldValue);
            npc.AiExtraData[node.Key] = AiSummon.ModifiedValue(hasOld, oldValue, node.Value, node.IsModify);
        }
    }

    private void ProcessNode(AnnounceNode node) {
        actor.Field.Broadcast(NpcNoticePacket.Announce(node.Message, node.DurationTick));
    }

    // timeTick (13 nodes: 80000, 300000, -30000, -90000) adds to or takes from the room's timer; isShowEffect (11)
    // shows the client's notice of the added or removed seconds. A field without a room timer has nothing to change.
    private void ProcessNode(ModifyRoomTimeNode node) {
        actor.Field.RoomTimer?.Modify(node.TimeTick, node.IsShowEffect);
    }

    // Every shown vibrate object of a hidden group fades out again (AiVibrate).
    private void ProcessNode(HideVibrateAllNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
        foreach (string vibrateId in actor.Field.Vibrate.HideAll()) {
            actor.Field.Broadcast(VibratePacket.Hide(vibrateId));
        }
    }

    private void ProcessNode(TriggerModifyUserValueNode node) {
        actor.Field.UserValues.Add(node.TriggerID, node.Key, node.Value);
    }

    // The one isKeepBattle node (a boss clearing its slaves just before a skill break) keeps the boss in battle.
    private void ProcessNode(RemoveSlavesNode node) {
        if (node.IsKeepBattle) {
            actor.BattleState.KeepBattle = true;
        }
        actor.RemoveSummons();
    }

    private void ProcessNode(CreateRandomRoomNode node) {
        actor.Field.SpawnRandomRoomPortal(node.RandomRoomId, node.PortalDuration, actor.Position, actor.Rotation);
    }

    private void ProcessNode(CreateInteractObjectNode node) {
        actor.Field.AddAiInteractObject(node, actor.Position, actor.Rotation);
    }

    private void ProcessNode(RemoveMeNode node) {
        actor.Field.RemoveNpc(actor.ObjectId);
    }

    private void ProcessNode(SuicideNode node) {
        actor.Stats.Values[BasicAttribute.Health].Current = 0;
    }


    private bool ProcessCondition(DistanceOverCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        // Both used to test the other way round.
        return AiDistance.Over(actor.Position, actor.BattleState.Target.Position, node.Value);
    }

    private bool ProcessCondition(CombatTimeCondition node) {
        return AiCombatTime.Passes(actor.Field.FieldTick - battleStartTick, node.BattleTimeBegin, node.BattleTimeLoop, node.BattleTimeEnd);
    }

    private bool ProcessCondition(DistanceLessCondition node) {
        if (actor.BattleState.Target is null) {
            return false;
        }

        // Both used to test the other way round.
        return AiDistance.Less(actor.Position, actor.BattleState.Target.Position, node.Value);
    }

    private bool ProcessCondition(SkillRangeCondition node) {
        SkillMetadata? skill = TryGetSkill(node.SkillIdx, node.SkillLev);

        if (skill is null) {
            return false;
        }

        if (actor.BattleState.Target is null) {
            return false;
        }

        // The skill's reach as the client reads it (AiSkillReach), not its detectProperty distance (0 in most npc skills).
        return AiSkillReach.InReach(actor.Position, actor.BattleState.Target.Position, AiSkillReach.Radius(skill.Data),
            AiSkillReach.Height(skill.Data));
    }

    private bool ProcessCondition(ExtraDataCondition node) {
        int value = 0;
        if (actor is FieldPet pet) {
            switch (node.Key) {
                case "tamingPoint":
                    value = pet.TamingPoint;
                    break;
                default: // aiPresets
                    return pet.Metadata.AiPresets.Contains(node.Key);
            }
        }

        return PerformOperation(node.Op, node.Value, actor.AiExtraData.GetValueOrDefault(node.Key, value));
    }

    private bool ProcessCondition(SlaveCountCondition node) {
        return AiSummon.SlaveCountPasses(node.Count, actor.SlaveCount(node.UseSummonGroup ? node.SummonGroup : null));
    }

    private bool ProcessCondition(SlaveCountOpCondition node) {
        return AiSummon.SlaveCountOpPasses(node.SlaveCountOp, node.SlaveCount, actor.SlaveCount(null));
    }

    private bool ProcessCondition(HpOverCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value <= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(StateCondition node) {
        if (actor.BattleState.Target is not FieldPlayer player) {
            return false;
        }
        return AiTargetState.Passes(node.TargetState, player.State, player.StateActorId, actor.ObjectId);
    }

    private bool ProcessCondition(AdditionalCondition node) {
        if (actor.BattleState.Target is null && node.IsTarget) {
            return false;
        }
        if (node.IsTarget) {
            return actor.BattleState.Target!.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
        }

        return actor.Buffs.HasBuff(node.Id, node.Level, node.OverlapCount);
    }

    private bool ProcessCondition(HpLessCondition node) {
        Stat health = actor.Stats.Values[BasicAttribute.Health];

        return node.Value >= ((float) health.Current / (float) health.Total) * 100f;
    }

    private bool ProcessCondition(TrueCondition node) {
        return true;
    }
}
