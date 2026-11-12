using System.Collections.Concurrent;
using DotRecast.Detour.Crowd;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Model.State;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Collision;
using Maple2.Tools.DotRecast;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;
using MovementState = Maple2.Server.Game.Model.ActorStateComponent.MovementState;

namespace Maple2.Server.Game.Model;

public class FieldNpc : Actor<Npc> {
    #region Control
    public bool SendControl;
    private long lastUpdate;

    private Vector3 velocity;
    private NpcState state;
    private short sequenceId;
    public override Vector3 Position { get => Transform.Position; set => Transform.Position = value; }
    public override Vector3 Rotation {
        get => Transform.RotationAnglesDegrees;
        set {
            Transform.RotationAnglesDegrees = value;
            SendControl = true;
        }
    }
    public Vector3 Velocity {
        get => velocity;
        set {
            if (value == velocity) return;
            velocity = value;
            SendControl = true;
        }
    }
    public NpcState State {
        get => state;
        [MemberNotNull(nameof(state))]
        set {
            state = value;
            SendControl = true;
        }
    }
    public short SequenceId {
        get => sequenceId;
        set {
            if (value == sequenceId) return;
            sequenceId = value;
            SendControl = true;
        }
    }
    public short SequenceCounter { get; set; }
    #endregion

    public required Vector3 Origin { get; init; }

    public FieldMobSpawn? Owner { get; init; }

    // The uid of the maid this npc is (FieldMaidPacket), 0 for other npcs.
    public long MaidUid { get; init; }

    // Npcs this npc summoned with an AI summon node, by the node's group.
    private readonly List<(int Group, FieldNpc Npc, bool LeavesWithSummoner)> summons = [];

    // The npc that summoned this one as its slave (a summon node without master="None"); null otherwise.
    public FieldNpc? Master { get; private set; }

    // The live slaves of this npc, in summon order.
    public List<FieldNpc> Slaves() {
        lock (summons) {
            PruneSummons();
            return summons.Where(summon => summon.LeavesWithSummoner).Select(summon => summon.Npc).ToList();
        }
    }

    public int SummonCount(int group) {
        lock (summons) {
            PruneSummons();
            return summons.Count(summon => summon.Group == group);
        }
    }

    // The live slaves (summons that leave with this npc), of one group or of all (group null).
    public int SlaveCount(int? group) {
        lock (summons) {
            PruneSummons();
            return summons.Count(summon => AiSummon.IsCountedSlave(summon.Group, summon.LeavesWithSummoner, group));
        }
    }

    private void PruneSummons() {
        summons.RemoveAll(summon => summon.Npc.IsDead || !Field.Mobs.ContainsKey(summon.Npc.ObjectId) && !Field.Npcs.ContainsKey(summon.Npc.ObjectId));
    }

    public void Summon(NpcMetadata npc, AiMetadata.SummonNode node) {
        if (IsDead) {
            return;
        }

        int count = AiSummon.Count(node.NpcCount, node.NpcCountMax, SummonCount(node.Group));
        for (int i = 0; i < count; i++) {
            Vector3 position = AiSummon.Position(Position, node.SummonPos, node.SummonPosOffset, node.SummonRadius, Random.Shared);
            if (Field.FindNearestPoly(position, out long nearestRef, out DotRecast.Core.Numerics.RcVec3f point) && nearestRef != 0) {
                position = DotRecastHelper.FromNavMeshSpace(point);
            }

            FieldNpc? summon = Field.SpawnNpc(npc, position, AiSummon.Rotation(Rotation, node.SummonPos, node.SummonRot));
            if (summon == null) {
                continue;
            }

            bool slave = AiSummon.LeavesWithSummoner(node.Master);
            if (slave) {
                summon.Master = this;
            }
            lock (summons) {
                summons.Add((node.Group, summon, slave));
            }
            Field.Broadcast(FieldPacket.AddNpc(summon));
            Field.Broadcast(ProxyObjectPacket.AddNpc(summon));
            summon.Update(Environment.TickCount64);

            TimeSpan lifeTime = AiSummon.LifeTime(node.LifeTime);
            if (lifeTime > TimeSpan.Zero) {
                Field.RemoveNpc(summon.ObjectId, lifeTime);
            }
        }
    }

    // The summons that leave with this npc (its slaves) are removed when it dies or leaves the field, and by the AI
    // RemoveSlaves node.
    public void RemoveSummons() {
        List<FieldNpc> leaving;
        lock (summons) {
            leaving = summons.Where(summon => summon.LeavesWithSummoner).Select(summon => summon.Npc).ToList();
            summons.RemoveAll(summon => summon.LeavesWithSummoner);
        }
        foreach (FieldNpc summon in leaving) {
            Field.RemoveNpc(summon.ObjectId);
        }
    }
    public override IPrism Shape => new Prism(
        new Circle(new Vector2(Position.X, Position.Y), Value.Metadata.Property.Capsule.Radius),
        Position.Z,
        Value.Metadata.Property.Capsule.Height
    );

    public readonly AgentNavigation? Navigation;
    public readonly AnimationSequenceMetadata IdleSequenceMetadata;
    public readonly AnimationSequenceMetadata? JumpSequence;
    public readonly AnimationSequenceMetadata? WalkSequence;
    public readonly AnimationSequenceMetadata? FlySequence;
    public readonly string? SpawnAnimation;
    private readonly WeightedSet<string> defaultRoutines;
    public AiState AiState { get; private set; }
    // The AI file the GM NPC control window asked for (SetDebugMode 4, AI file). The field thread takes it
    // at the NPC's next update, so the AI is never replaced while it runs.
    private string? pendingAiFile;
    public string? PendingAiFile => Volatile.Read(ref pendingAiFile);
    public void RequestAiFile(string name) => Volatile.Write(ref pendingAiFile, name);

    public readonly MovementState MovementState;
    public readonly BattleState BattleState;
    public readonly TaskState TaskState;
    public readonly SkillMetadata?[] Skills;

    public int SpawnPointId = 0;
    // The score of the spawn_npc_range that spawned it, added to the open score board when it dies.
    public int TriggerScore;

    // Set by the field when it spawns this npc: the npc's own additionalEffect list is added on its first update,
    // after the spawn was announced.
    public bool SpawnBuffsPending;
    // InteractNpc: the field tick from which the npc is talkable again, and whether the field still has to hear it.
    public long InteractReadyTick { get; private set; }
    private bool interactCoolingDown;
    public bool Talkable => NpcInteractRules.Talkable(Value.Metadata.Interact, InteractReadyTick, Field.FieldTick);

    // A player's interact cast completed: the npc casts its interact skill and is not talkable for its cool time.
    public void Interact(NpcMetadataInteract interact) {
        if (interact.CoolTime > 0) {
            InteractReadyTick = Field.FieldTick + interact.CoolTime;
            interactCoolingDown = true;
            Field.Broadcast(FieldPacket.InteractNpc(ObjectId, false));
        }
        CastAiSkill(interact.SkillId, interact.SkillLevel, faceTarget: 0, facePos: default);
    }

    public bool IsCorpse { get; private set; }
    // A story ally that damage does not kill (npc propertyTags no_die, NpcTargetRules.HealthChange). A pet never is.
    // A marker a script waits on, which no monster fights (NpcTargetRules.IsScriptMarker).
    public bool IsScriptMarker => NpcTargetRules.IsScriptMarker(Value.Metadata.Model.Name,
        Value.Metadata.Basic.HasPropertyTag("big_hp_bar") || Value.Metadata.Basic.HasPropertyTag("always_display_hp_bar"));
    public bool NoDie => this is not FieldPet && Value.Metadata.Basic.HasPropertyTag("no_die");
    private long lastCorpseBroadcastTick;
    private byte lastControlFlags;
    // The flags byte of this npc's NpcControl entries.
    public byte ControlFlags => NpcControlRules.Flags(BattleState.InBattle, MovementState.State == ActorState.Stun);
    public Action<FieldNpc>? WorldBossDeathCallback { get; set; }
    public long LastDamageTick { get; private set; }
    private int lastAttackerObjectId;

    public MS2PatrolData? Patrol { get; private set; }
    private int currentWaypointIndex;

    private bool hasBeenBattling;
    private NpcTask? idleTask;
    private long idleTaskLimitTick;

    // Written on the field thread (AI nodes, triggers) and read from a session thread by the GM NPC control
    // window, so it is a store that is safe to read while it changes.
    public readonly ConcurrentDictionary<string, int> AiExtraData = new();
    // Set by the GM NPC control window (SetDebugMode 4, AI off): the NPC stops choosing what to do next.
    // What it already does (a move, a cast) runs to its end.
    public volatile bool AiOff;

    public FieldNpc(FieldManager field, int objectId, DtCrowdAgent? agent, Npc npc, string aiPath, string spawnAnimation = "", string? patrolDataUUID = null) : base(field, objectId, npc, field.NpcMetadata) {
        IdleSequenceMetadata = npc.Animations.GetValueOrDefault("Idle_A") ?? new AnimationSequenceMetadata(string.Empty, -1, 1f, []);
        JumpSequence = npc.Animations.GetValueOrDefault("Jump_A") ?? npc.Animations.GetValueOrDefault("Jump_B");
        WalkSequence = npc.Animations.GetValueOrDefault("Walk_A");
        FlySequence = npc.Animations.GetValueOrDefault("Fly_A");
        SpawnAnimation = spawnAnimation;
        defaultRoutines = new WeightedSet<string>();
        foreach (NpcAction action in Value.Metadata.Action.Actions) {
            defaultRoutines.Add(action.Name, action.Probability);
        }

        if (agent is not null) {
            Navigation = Field.Navigation.ForAgent(this, agent);

            if (patrolDataUUID is not null) {
                Patrol = field.Entities.Patrols.FirstOrDefault(x => x.Uuid == patrolDataUUID);
            }
        }
        MovementState = new MovementState(this);
        BattleState = new BattleState(this);
        TaskState = new TaskState(this);
        State = new NpcState();
        SequenceId = -1;
        SequenceCounter = 1;
        AiState = new AiState(this, aiPath);

        Skills = new SkillMetadata[Value.Metadata.Skill.Entries.Length];

        for (int i = 0; i < Skills.Length; ++i) {
            var entry = Value.Metadata.Skill.Entries[i];
            Field.SkillMetadata.TryGet(entry.Id, entry.Level, out Skills[i]);
        }
    }

    protected override void Dispose(bool disposing) { }

    protected virtual void Remove(TimeSpan delay) => Field.RemoveNpc(ObjectId, delay);

    private List<string> debugMessages = [];
    private bool playersListeningToDebug = false; // controls whether messages should log
    private long nextDebugPacket = 0;

    // The npc data's additionalEffect codes and levels: each is added to the npc, cast by itself. A code the
    // effect data does not have is left out.
    private void AddSpawnBuffs(long tickCount) {
        foreach (NpcMetadataBuff buff in Value.Metadata.Property.Buffs) {
            if (!Field.SkillMetadata.TryGetEffect(buff.Id, (short) buff.Level, out _)) {
                continue;
            }
            Buffs.AddBuff(this, this, buff.Id, (short) buff.Level, tickCount);
        }
    }

    public override void Update(long tickCount) {
        if (IsDead) {
            if (IsCorpse && tickCount - lastCorpseBroadcastTick >= 1000) {
                lastCorpseBroadcastTick = tickCount;
                SequenceCounter++;
                Field.Broadcast(NpcControlPacket.Dead(this));
            }
            return;
        }

        if (SpawnBuffsPending) {
            SpawnBuffsPending = false;
            AddSpawnBuffs(tickCount);
        }

        base.Update(tickCount);

        if (interactCoolingDown && tickCount >= InteractReadyTick) {
            interactCoolingDown = false;
            Field.Broadcast(FieldPacket.InteractNpc(ObjectId, true));
        }

        // controls whether currently logged messages should print
        bool playersListeningToDebugNow = false;

        foreach ((int objectId, FieldPlayer player) in Field.Players) {
            if (player.DebugAi) {
                playersListeningToDebugNow = true;

                break;
            }
        }

        MovementState.ApplyHits();
        bool isSpawning = MovementState.State is ActorState.Spawn or ActorState.Regen;
        bool isStunned = false;

        if (!isSpawning) {
            string? aiFile = Interlocked.Exchange(ref pendingAiFile, null);
            if (aiFile != null) {
                AiState = new AiState(this, aiFile);
            }
            BattleState.Update(tickCount);
            MovementState.SetStunned(Buffs.Stunned);
            isStunned = MovementState.State is ActorState.Stun or ActorState.Hit; // held: no AI, no tasks
            if (!AiOff && !isStunned) {
                AiState.Update(tickCount);
                DoIdleBehavior(tickCount);
            }
        }

        MovementState.Update(tickCount);

        if (!isSpawning && !isStunned) {
            TaskState.Update(tickCount);
        }

        bool sentDebugPacket = false;

        if (tickCount >= nextDebugPacket && playersListeningToDebugNow && debugMessages.Count > 0) {
            sentDebugPacket = true;

            Field.BroadcastAiMessage(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, ObjectId, string.Join("", debugMessages.ToArray()), 2500, 0));
        }

        if (sentDebugPacket || tickCount >= nextDebugPacket) {
            debugMessages.Clear();
        }

        playersListeningToDebug = playersListeningToDebugNow;

        // The battle flag goes out when it changes.
        byte controlFlags = ControlFlags;
        SendControl |= controlFlags != lastControlFlags;
        lastControlFlags = controlFlags;

        if (SendControl && !IsDead) {
            SequenceCounter++;
            Field.BroadcastNpcControl(this);
            MovementState.HitSent();
            SendControl = false;
        }
        lastUpdate = tickCount;
    }

    private void DoIdleBehavior(long tickCount) {
        hasBeenBattling |= BattleState.InBattle;

        if (BattleState.InBattle) {
            idleTask?.Cancel();
            idleTask = null;

            return;
        }

        if (hasBeenBattling && idleTask is null && MovementState.CastTask is null) {
            Vector3 spawnPoint = Navigation?.GetRandomPatrolPoint() ?? Origin;

            idleTask = MovementState.TryMoveTo(spawnPoint, false);
            hasBeenBattling = false;
        }

        if (idleTask is MovementState.NpcStandbyTask && idleTaskLimitTick == 0) {
            idleTaskLimitTick = tickCount + 1000;
        } else if (idleTask is not MovementState.NpcStandbyTask && idleTaskLimitTick != 0) {
            idleTaskLimitTick = 0;
        }

        bool hitLimit = idleTaskLimitTick != 0 && tickCount >= idleTaskLimitTick;

        if (!hasBeenBattling && (idleTask is null || idleTask.IsDone || hitLimit)) {
            idleTaskLimitTick = 0;

            idleTask = NextRoutine(tickCount);
        }
    }

    public override void KeyframeEvent(string keyName) {
        MovementState.KeyframeEvent(keyName);
    }

    private NpcTask? NextRoutine(long tickCount) {
        // A summoned pet keeps up with its owner instead of wandering around where it was summoned.
        if (this is FieldPet { OwnerId: not 0 } summoned) {
            return summoned.NextFollowTask();
        }

        if (Patrol?.WayPoints.Count > 0 && Navigation is not null) {
            return NextWaypoint();
        }

        string routineName = defaultRoutines.Get();
        if (!Value.Animations.TryGetValue(routineName, out AnimationSequenceMetadata? sequence)) {
            Logger.Error("Invalid routine: {Routine} for npc {NpcId}", routineName, Value.Metadata.Id);

            return MovementState.TryStandby(null, true);
        }

        switch (routineName) {
            case not null when routineName.Contains("Idle_"):
                return MovementState.TryStandby(null, true, sequence.Name);
            case not null when routineName.Contains("Bore_"):
                return MovementState.TryEmote(sequence.Name, true);
            case not null when routineName.StartsWith("Walk_"):
            case not null when routineName.StartsWith("Run_"):
                return MovementState.TryMoveTo(Navigation?.GetRandomPatrolPoint() ?? Position, false, sequence.Name);
            case not null:
                if (!Value.Animations.TryGetValue(routineName, out AnimationSequenceMetadata? animationSequence)) {
                    break;
                }
                return MovementState.TryEmote(animationSequence.Name, SpawnAnimation is not null);
        }

        Logger.Warning("Unhandled routine: {Routine} for npc {NpcId}", routineName, Value.Metadata.Id);

        return MovementState.TryStandby(null, true);
    }

    private NpcTask? NextWaypoint() {
        MS2WayPoint currentWaypoint = Patrol!.WayPoints[currentWaypointIndex];
        MS2WayPoint? waypointBefore = null;
        if (Patrol.IsLoop) {
            waypointBefore = Patrol.WayPoints[(currentWaypointIndex - 1 + Patrol.WayPoints.Count) % Patrol.WayPoints.Count];
        } else if (currentWaypointIndex != 0) {
            waypointBefore = Patrol.WayPoints[currentWaypointIndex - 1];
        }

        if (waypointBefore is not null && !string.IsNullOrEmpty(waypointBefore.ArriveAnimation) && idleTask is not (MovementState.NpcEmoteTask or null)) {
            if (Value.Animations.TryGetValue(waypointBefore.ArriveAnimation, out AnimationSequenceMetadata? arriveSequence)) {
                return MovementState.TryEmote(arriveSequence.Name, false);
            }
        }

        NpcTask? approachTask = null;

        if (currentWaypoint.AirWayPoint) {
            if (Value.Animations.TryGetValue(currentWaypoint.ApproachAnimation, out AnimationSequenceMetadata? patrolSequence)) {
                approachTask = MovementState.TryFlyTo(currentWaypoint.Position, false, sequence: patrolSequence.Name, speed: (float) Patrol.PatrolSpeed / 2, lookAt: true);
            } else if (FlySequence is not null) {
                approachTask = MovementState.TryFlyTo(currentWaypoint.Position, false, sequence: FlySequence.Name, speed: (float) Patrol.PatrolSpeed / 2, lookAt: true);
            } else {
                Logger.Warning("No walk sequence found for npc {NpcId} in patrol {PatrolId}", Value.Metadata.Id, Patrol.Uuid);
            }
        } else {
            if (Navigation!.PathTo(currentWaypoint.Position)) {
                if (Value.Animations.TryGetValue(currentWaypoint.ApproachAnimation, out AnimationSequenceMetadata? patrolSequence)) {
                    approachTask = MovementState.TryMoveTo(currentWaypoint.Position, false, sequence: patrolSequence.Name, speed: 1);
                } else if (WalkSequence is not null) {
                    approachTask = MovementState.TryMoveTo(currentWaypoint.Position, false, sequence: WalkSequence.Name, speed: 1);
                } else {
                    Logger.Warning("No walk sequence found for npc {NpcId} in patrol {PatrolId}", Value.Metadata.Id, Patrol.Uuid);
                }
            } else if (Navigation.GoalOffMesh(currentWaypoint.Position)) {
                // The waypoint stands on ground the navmesh does not cover (e.g. a train track that is not path geometry):
                // go there in a straight line at walk speed instead of skipping it, so boxes waiting for the npc see it.
                if (Value.Animations.TryGetValue(currentWaypoint.ApproachAnimation, out AnimationSequenceMetadata? patrolSequence)) {
                    approachTask = MovementState.TryFlyTo(currentWaypoint.Position, false, sequence: patrolSequence.Name, speed: 1);
                } else if (WalkSequence is not null) {
                    approachTask = MovementState.TryFlyTo(currentWaypoint.Position, false, sequence: WalkSequence.Name, speed: 1);
                } else {
                    Logger.Warning("No walk sequence found for npc {NpcId} in patrol {PatrolId}", Value.Metadata.Id, Patrol.Uuid);
                }
            } else {
                Logger.Warning("Failed to path to waypoint id({Id}) coord {Coord} for npc {NpcName} - {NpcId} in patrol {PatrolId}", currentWaypoint.Id, currentWaypoint.Position, Value.Metadata.Name, Value.Metadata.Id, Patrol.Uuid);
            }
        }

        MS2WayPoint lastWaypoint = Patrol.WayPoints.Last();

        // if we're at the last waypoint and we're not looping, we're done
        if (currentWaypoint.Id == lastWaypoint.Id && !Patrol.IsLoop) {
            Patrol = null;

            return approachTask;
        }

        currentWaypointIndex = (currentWaypointIndex + 1) % Patrol.WayPoints.Count;

        if ((approachTask?.Status ?? NpcTaskStatus.Cancelled) == NpcTaskStatus.Cancelled) {
            Logger.Warning("Failed to path to waypoint id({Id}) coord {Coord} for npc {NpcName} - {NpcId} in patrol {PatrolId}", currentWaypoint.Id, currentWaypoint.Position, Value.Metadata.Name, Value.Metadata.Id, Patrol.Uuid);
            return MovementState.TryStandby(null, true);
        }

        return approachTask;
    }

    // Damage taken since spawn, shown by the top hp bar of an npc with the damaged_hp_upper_hp_bar tag.
    public long AccumulatedDamage { get; private set; }

    protected override void OnDamageReceived(IActor caster, long amount) {
        LastDamageTick = Environment.TickCount64;
        AccumulatedDamage += amount;
        if (Value.Metadata.Basic.HasPropertyTag("damaged_hp_upper_hp_bar")) {
            Field.Broadcast(FieldPacket.NpcAccumulatedDamage(this));
        }
        lastAttackerObjectId = caster.ObjectId;
        if (caster is FieldPlayer hitPlayer) {
            DropHitLoot(hitPlayer);
            if (Field is DungeonFieldManager dungeonField) {
                dungeonField.CountMissions(hitPlayer, mission => mission.AddDamage(Value.Metadata.Id, amount));
                if (Value.IsBoss) {
                    dungeonField.DungeonRoomRecord.AddBossDamage(hitPlayer.Value.Character.Id, amount);
                }
            }
        }
    }

    protected override void OnDeath() {
        WorldBossDeathCallback?.Invoke(this);
        Owner?.Despawn(ObjectId, Value.Metadata.Dead.Time);
        RemoveSummons();
        SendControl = false;

        SequenceCounter++;
        Field.Broadcast(NpcControlPacket.Dead(this));
        // After RemoveSummons: 44 battleEnd trees summon at the death.
        AiState.RunDeathBattleEnd();

        if (Value.Metadata.Corpse?.HitAble == true) {
            IsCorpse = true;
            lastCorpseBroadcastTick = Environment.TickCount64;
        }

        HandleDamageDealers();
        DarkStreamSpawnCleared();
        Field.StringIdDeaths.Died(Value.Metadata.Basic.StringId);
        Field.AddScoreBoardScore(TriggerScore);
        if (GetLastAttacker() is FieldPlayer lastHitter) {
            // The killing blow counts for npc_lasthit, with the NPC id as the code (field boss achievements).
            lastHitter.Session.ConditionUpdate(ConditionType.npc_lasthit, codeLong: Value.Id, targetLong: Field.MapId);
            if (Field is DungeonFieldManager dungeonField) {
                dungeonField.CountMissions(lastHitter, mission => mission.AddLastHit(Value.Metadata.Id));
            }
        }

        Remove(delay: TimeSpan.FromSeconds(Value.Metadata.Dead.Time));
    }

    // Dark Stream: the last monster of a scored spawn point died, so the run gets the spawn's score.
    private void DarkStreamSpawnCleared() {
        if (Field.DarkStream is not { } game || SpawnPointId == 0) {
            return;
        }
        if (Field.Mobs.Values.Any(mob => mob != this && mob.SpawnPointId == SpawnPointId && !mob.IsDead)) {
            return;
        }
        if (game.SpawnCleared(SpawnPointId)) {
            Field.Broadcast(DarkStreamPacket.Score(game.Score));
        }
    }

    public virtual void Animate(string sequenceName, float duration = -1f) {
        if (!Value.Animations.TryGetValue(sequenceName, out AnimationSequenceMetadata? sequence)) {
            Logger.Error("Invalid sequence: {Sequence} for npc {NpcId}", sequenceName, Value.Metadata.Id);
            return;
        }

        idleTask = MovementState.TryEmote(sequence.Name, IsIdleSequence(sequenceName), duration);
    }

    /// <summary>Plays a set_npc_emotion_sequence list (see <see cref="TriggerSequenceList"/>): each name once, in order.</summary>
    public void AnimateSequence(string sequenceNames) {
        var missing = new List<string>();
        string[] names = TriggerSequenceList.Playable(TriggerSequenceList.Split(sequenceNames),
            name => Value.Animations.TryGetValue(name, out AnimationSequenceMetadata? sequence) ? sequence.Name : null, missing);
        if (missing.Count > 0) {
            Logger.Error("Invalid sequence: {Sequence} for npc {NpcId}", string.Join(",", missing), Value.Metadata.Id);
        }
        if (names.Length == 0) {
            return;
        }

        idleTask = MovementState.TryEmote(names, IsIdleSequence(names[0]));
    }

    private static bool IsIdleSequence(string sequenceName) =>
        sequenceName.Contains("idle", StringComparison.OrdinalIgnoreCase) || sequenceName.Contains("sit", StringComparison.OrdinalIgnoreCase);

    public void Talk() {
        if (!string.IsNullOrEmpty(SpawnAnimation)) {
            return;
        }
        idleTask = MovementState.TryTalk();
    }

    public void StopTalk() {
        if (idleTask is MovementState.NpcTalkTask) {
            idleTask.Cancel();
        }
    }

    public override void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        if (IsCorpse) {
            // Corpse loot intentionally drops on every hit — players are expected to keep
            // attacking the corpse to collect loot. A per-player rate limit (e.g. once per
            // second) could be added here if spamming turns out to be an issue, but whether
            // the original server enforced one is unknown.
            if (caster is FieldPlayer player) {
                DropCorpseLoot(player);
            }
            SequenceCounter++;
            Field.Broadcast(NpcControlPacket.CorpseHit(this));
            return;
        }
        base.ApplyDamage(caster, damage, attack);
        if (!IsDead && damage.Targets.TryGetValue(ObjectId, out DamageRecordTarget? hit) && hit.Damage.Any(entry => entry.Amount != 0)) {
            MovementState.QueueHit(hit.Position, attack); // applied by the field thread in Update
        }
    }

    // Drop box semantics (confirmed via KMS2 video analysis):
    // - GlobalDropBoxIds       : drops spawned on death, rolled once per damage dealer and locked to it.
    // - GlobalHitDropBoxIds    : drops spawned each time the NPC is hit while alive (triggered in OnDamageReceived),
    //                            locked to the player who hit.
    // - DeadGlobalDropBoxIds   : drops spawned when a player hits the NPC corpse (IsCorpse == true).
    // - IndividualDropBoxIds   : per-player drops on death (each damage dealer gets their own).
    // - IndividualHitDropBoxIds: per-player drops each time the NPC is hit while alive.
    //
    // NOTE on globalDropItemBox vs globalDropItemSet naming:
    // NPC XML uses globalDropBoxId which references a DROP BOX (defined by dropBoxID in globalDropItemBox).
    // Each drop box then references item GROUPs (defined by dropGroupID in globalDropItemSet).
    // These are separate namespaces — e.g. drop BOX 4 (Doondun's death box) contains only mesos and
    // CN-locale items, while item GROUP 4 ("boss equipment drop") is a completely different entity
    // only reachable via drop BOX 10, which no NPC uses. Equipment drops for bosses come from
    // IndividualDropBoxIds instead (keyed to the NPC id, e.g. individualDropBoxId="23000013" for Doondun).
    private void DropGlobalLoot(FieldPlayer receiver) {
        NpcMetadataDropInfo dropInfo = Value.Metadata.DropInfo;
        var globalDrops = new List<Item>();
        foreach (int globalDropId in dropInfo.GlobalDropBoxIds) {
            globalDrops.AddRange(Field.ItemDrop.GetGlobalDropItems(globalDropId, Value.Metadata.Basic.Level));
        }

        foreach (Item item in globalDrops) {
            ApplyMesoRate(receiver, item);
            float x = Random.Shared.Next((int) Position.X - dropInfo.DropDistanceRandom, (int) Position.X + dropInfo.DropDistanceRandom);
            float y = Random.Shared.Next((int) Position.Y - dropInfo.DropDistanceRandom, (int) Position.Y + dropInfo.DropDistanceRandom);
            Field.DropItem(new Vector3(x, y, Position.Z), Rotation, item, owner: this, characterId: receiver.Value.Character.Id, dropKind: ItemDropKind.NpcDeath);
        }
    }

    private void DropHitLoot(FieldPlayer player) {
        NpcMetadataDropInfo dropInfo = Value.Metadata.DropInfo;
        var globalDrops = new List<Item>();
        foreach (int globalHitDropId in dropInfo.GlobalHitDropBoxIds) {
            globalDrops.AddRange(Field.ItemDrop.GetGlobalDropItems(globalHitDropId, Value.Metadata.Basic.Level));
        }

        foreach (Item item in globalDrops) {
            float x = Random.Shared.Next((int) Position.X - dropInfo.DropDistanceRandom, (int) Position.X + dropInfo.DropDistanceRandom);
            float y = Random.Shared.Next((int) Position.Y - dropInfo.DropDistanceRandom, (int) Position.Y + dropInfo.DropDistanceRandom);
            Field.DropItem(new Vector3(x, y, Position.Z), Rotation, item, owner: this, characterId: player.Value.Character.Id, dropKind: ItemDropKind.NpcHit);
        }

        foreach (int individualHitDropId in dropInfo.IndividualHitDropBoxIds) {
            foreach (Item item in Field.ItemDrop.GetIndividualDropItems(player.Session, player.Value.Character.Level, individualHitDropId)) {
                float x = Random.Shared.Next((int) Position.X - dropInfo.DropDistanceRandom, (int) Position.X + dropInfo.DropDistanceRandom);
                float y = Random.Shared.Next((int) Position.Y - dropInfo.DropDistanceRandom, (int) Position.Y + dropInfo.DropDistanceRandom);
                Field.DropItem(new Vector3(x, y, Position.Z), Rotation, item, owner: this, characterId: player.Value.Character.Id, dropKind: ItemDropKind.NpcHit);
            }
        }
    }

    private void DropIndividualLoot(FieldPlayer player) {
        NpcMetadataDropInfo dropInfo = Value.Metadata.DropInfo;
        var individualDrops = new List<Item>();
        foreach (int individualDropId in dropInfo.IndividualDropBoxIds) {
            individualDrops.AddRange(Field.ItemDrop.GetIndividualDropItems(player.Session, player.Value.Character.Level, individualDropId));
        }

        foreach (Item item in individualDrops) {
            ApplyMesoRate(player, item);
            float x = Random.Shared.Next((int) Position.X - dropInfo.DropDistanceRandom, (int) Position.X + dropInfo.DropDistanceRandom);
            float y = Random.Shared.Next((int) Position.Y - dropInfo.DropDistanceRandom, (int) Position.Y + dropInfo.DropDistanceRandom);
            Field.DropItem(new Vector3(x, y, Position.Z), Rotation, item, owner: this, characterId: player.Value.Character.Id, dropKind: ItemDropKind.NpcDeath);
        }
    }

    // The meso rate (SpecialAttribute.Meso: the party buff, gear options) raises the mesos a kill drops for the player.
    private static void ApplyMesoRate(FieldPlayer player, Item item) {
        if (item.IsMeso()) {
            item.Amount = KillRate.Apply(item.Amount, player.Stats.Values[SpecialAttribute.Meso].Multiplier());
        }
    }

    public void DropCorpseLoot(FieldPlayer player) {
        NpcMetadataDropInfo dropInfo = Value.Metadata.DropInfo;
        var globalDrops = new List<Item>();
        foreach (int deadGlobalDropId in dropInfo.DeadGlobalDropBoxIds) {
            globalDrops.AddRange(Field.ItemDrop.GetGlobalDropItems(deadGlobalDropId, Value.Metadata.Basic.Level));
        }

        foreach (Item item in globalDrops) {
            float x = Random.Shared.Next((int) Position.X - dropInfo.DropDistanceRandom, (int) Position.X + dropInfo.DropDistanceRandom);
            float y = Random.Shared.Next((int) Position.Y - dropInfo.DropDistanceRandom, (int) Position.Y + dropInfo.DropDistanceRandom);
            Field.DropItem(new Vector3(x, y, Position.Z), Rotation, item, owner: this, characterId: player.Value.Character.Id, dropKind: ItemDropKind.NpcHit);
        }
    }

    public override SkillRecord? CastSkill(int id, short level, long uid, int castTick, in Vector3 position = default, in Vector3 direction = default, in Vector3 rotation = default, float rotateZ = 0f, byte motionPoint = 0) {
        if (!Field.SkillMetadata.TryGet(id, level, out SkillMetadata? metadata) || metadata.Data.Motions.Length <= motionPoint) {
            Logger.Error("Invalid skill use: {SkillId},{Level},{motionPoint}", id, level, motionPoint);
            return null;
        }

        if (uid == 0) {
            // The client derives the player's skill cast skillSn/uid using this formula so I'm using it here for mob casts for parity.
            uid = (long) NextLocalId() << 32 | (uint) Environment.TickCount;
        }

        Field.Broadcast(NpcControlPacket.Control(this));

        return base.CastSkill(id, level, uid, castTick, position, direction, rotation, rotateZ, motionPoint);
    }

    public NpcTask CastAiSkill(int id, short level, int faceTarget, Vector3 facePos, long uid = 0) {
        return MovementState.TryCastSkill(id, level, faceTarget, facePos, uid);
    }

    // mob drops, exp, etc.
    private void HandleDamageDealers() {
        // Death drops, boss or mob: each damage dealer gets its own, locked to it. KMS2 sends them as type 0 with the
        // dealer's character id; in a party only the members who dealt damage get one.
        SendKillNotices();
        foreach (KeyValuePair<int, DamageRecordTarget> damageDealer in DamageDealers) {
            if (!Field.TryGetPlayer(damageDealer.Key, out FieldPlayer? player)) {
                continue;
            }

            DropGlobalLoot(player);
            DropIndividualLoot(player);
            // KMS2: a dealer who is dead when the npc dies still gets its drop box, but no kill exp.
            if (BossKillRules.DealerGetsExp(player.IsDead)) {
                GiveExp(player);
            }

            player.Session.ConditionUpdate(ConditionType.npc, codeLong: Value.Id, targetLong: Field.MapId);
            player.Session.Exp.OnKill(Value.Metadata.Basic.Level, Environment.TickCount64);
            foreach (string tag in Value.Metadata.Basic.MainTags) {
                player.Session.ConditionUpdate(ConditionType.npc_race, codeString: tag);
            }
            UpdateSpawner(player);
        }
    }

    // KMS2 sends, ahead of the kill exp: AppendMessageAssistBonus to the field boss's dealers, then AppendMessageKillBoss
    // with the last hitter's name for an elite or boss kill. The notice goes to the field (chosen here: the captures show
    // other players' kills but not whether they were all in the party).
    // The dealer told of the assist bonus counts for npc_assist_bonus with the boss as the code.
    internal static void ReportAssistBonus(GameSession session, int npcId) {
        session.ConditionUpdate(ConditionType.npc_assist_bonus, codeLong: npcId);
    }

    private void SendKillNotices() {
        int npcClass = Value.Metadata.Basic.Class;
        if (BossKillRules.FieldBoss(npcClass)) {
            foreach (int dealerId in DamageDealers.Keys) {
                if (Field.TryGetPlayer(dealerId, out FieldPlayer? dealer) && BossKillRules.DealerGetsExp(dealer.IsDead)) {
                    dealer.Session.Send(AppendMessagePacket.AssistBonus(Value.Id));
                    ReportAssistBonus(dealer.Session, Value.Id);
                }
            }
        }
        if (BossKillRules.KillNotice(npcClass) && GetLastAttacker() is FieldPlayer killer) {
            Field.Broadcast(AppendMessagePacket.KillBoss(killer.Value.Character.Name, Value.Id));
        }
    }

    // A kill of a mob of the field's region spawns (MS2RegionSpawn) counts for the spawner condition: its code is the
    // region spawn id, its target the map. NPCs a trigger spawned have no region spawn.
    private void UpdateSpawner(FieldPlayer player) {
        if (Owner == null) {
            return;
        }
        player.Session.ConditionUpdate(ConditionType.spawner, codeLong: Owner.Value.Id, targetLong: Field.MapId);
    }

    private void GiveExp(FieldPlayer player) {
        // 0 means no exp
        if (Value.Metadata.Basic.CustomExp == 0) {
            return;
        }

        // The exp rate (SpecialAttribute.Experience: the party buff, gear options) raises the exp of a kill.
        double expRate = player.Stats.Values[SpecialAttribute.Experience].Multiplier();
        player.Session.Pet?.AddKillExp(Value.Metadata.Basic.Level, Value.Metadata.Basic.Class);

        // KMS2 gives every dealer the full kill exp, nothing is split; the ExpUp carries the npc's object id.
        long killExp = Value.Metadata.Basic.CustomExp;
        if (killExp == -1) {
            killExp = player.Session.Exp.MobExp(Value.Metadata.Basic.Level, Value.Metadata.Basic.Class);
            player.Session.Exp.AddMobExp(Value.Metadata.Basic.Level, Value.Metadata.Basic.Class, modifier: (float) Math.Max(0d, 1d + expRate), npcObjectId: ObjectId);
        } else {
            player.Session.Exp.AddCustomKillExp(KillRate.Apply(killExp, expRate), ObjectId);
        }

        if (BossKillRules.FieldBoss(Value.Metadata.Basic.Class)) {
            player.Session.Exp.AddFieldBossBonusExp(killExp, ObjectId);
        }
    }

    public void SendDebugAiInfo(GameSession requester) {
        string message = $"{ObjectId}";
        message += "\n" + (AiState.AiMetadata?.Name ?? "[No AI]");
        if (this is FieldPet pet) {
            if (Field.TryGetPlayer(pet.OwnerId, out FieldPlayer? player)) {
                message += "\nOwner: " + player.Value.Character.Name;
            }
        }
        requester.Send(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, ObjectId, message, 2500, 0));
    }

    public void AppendDebugMessage(string message, bool sanitize = false) {
        if (!playersListeningToDebug) {
            return;
        }

        if (sanitize) {
            message = message.Replace("<", "&lt;").Replace(">", "&gt;");
        }

        if (debugMessages.Count > 0 && debugMessages.Last().Last() != '\n') {
            debugMessages.Add("\n");
        }

        debugMessages.Add(message);

        if (debugMessages.Last().Last() != '\n') {
            Field.BroadcastAiMessage(NoticePacket.Message($"{ObjectId}: {message}"));
        } else {
            string trimmedMessage = message.Substring(0, Math.Max(0, message.Length - 1));

            Field.BroadcastAiMessage(NoticePacket.Message($"{ObjectId}: {trimmedMessage}"));
        }
    }

    public void SetPatrolData(MS2PatrolData newPatrolData) {
        Patrol = newPatrolData;
        currentWaypointIndex = 0;
    }

    public void ClearPatrolData() {
        if (Patrol is null) {
            return;
        }

        MS2WayPoint currentWaypoint = Patrol.WayPoints[currentWaypointIndex];

        // make sure we're at the last checkpoint in the list
        if (currentWaypoint.Id != Patrol.WayPoints.Last().Id) {
            return;
        }

        // Clear patrol data
        Patrol = null;
        currentWaypointIndex = 0;
    }

    public FieldPlayer? GetLastAttacker() {
        if (lastAttackerObjectId == 0 || !Field.TryGetPlayer(lastAttackerObjectId, out FieldPlayer? player)) {
            return null;
        }
        return player;
    }

    public override string ToString() {
        return $"FieldNpc(Id: {Value.Metadata.Id}, Name: {Value.Metadata.Name}, State: {State}, SequenceId: {SequenceId}, SequenceCounter: {SequenceCounter})";
    }
}
