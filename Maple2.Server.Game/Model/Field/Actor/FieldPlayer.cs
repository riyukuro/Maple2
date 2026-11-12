using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Model.Validators;
using Maple2.Server.Core.Network;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using Maple2.Tools.Scheduler;
using System.Numerics;

namespace Maple2.Server.Game.Model;

public class FieldPlayer : Actor<Player> {
    public readonly GameSession Session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => Session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public Vector3 LastGroundPosition;
    public readonly MoveWindow Moves = new(); // the positions the client synced in the last second

    public override StatsManager Stats => Session.Stats;
    public override BuffManager Buffs => Session.Buffs;
    public override IPrism Shape => new Prism(new Circle(new Vector2(Position.X, Position.Y), 10), Position.Z, 100);
    private ActorState state;
    public ActorState State {
        get => state;
        set {
            if (state == value) return;
            state = value;
            Flag |= PlayerObjectFlag.State;
            StateSyncTimeTracking = 0; // Reset time tracking on state change
        }
    }
    // The actor the last state sync names for a hold or a grab (AiTargetState), else 0.
    public int StateActorId { get; private set; }
    private bool isDead;
    public override bool IsDead {
        get => isDead;
        protected set {
            if (Session.Field is null) return;
            if (value == isDead) return;
            isDead = value;
            Flag |= PlayerObjectFlag.Dead;
            if (!isDead) {
                DeathState = DeathState.Alive;
            } else {
                if (Session.Field.Metadata.Property.OnlyDarkTomb) {
                    DeathState = DeathState.Metal;
                } else {
                    DeathState = Session.Config.DeathCount == 0 ? DeathState.FirstDeath : DeathState.Metal;
                }
            }
        }
    }
    public ActorSubState SubState { get; set; }
    public PlayerObjectFlag Flag { get; set; }
    private long flagTick;
    // Fields with autoRevivalType 1 or 2 and an autoRevivalTime: the client opens no revival dialog there
    // (MyPC::vfunc_137, mypc.cpp:1351), so the server revives the player when the time is over.
    private int autoReviveDelay;
    private long autoReviveTick;
    // The client makes a revived player invincible for UserRevivalInvincibleTick (CSceneStateGame::OnRevival,
    // scenestategame.cpp:7408). Monster damage is ours, so the server keeps the same window.
    private long revivalInvincibleUntil;
    private readonly HealthSyncClock healthSync = new();
    private long healthSyncTick; // the tick of the last Update

    private long battleTick;
    // The party buffs follow the party members in the field; checked once a second.
    private const int PartyBuffIntervalMs = 1000;
    private long partyBuffTick;
    // At home rest exp grows every RestExpRules.TickMs, the first time one tick after entering.
    private long restExpTick;

    // The emotion skill of the last state sync (0: none), the tick its time was last counted at and the time not
    // counted yet (see TrophyTimeRules).
    private int emotionSkillId;
    private long emotionTick;
    private long emotionMs;
    private bool inBattle;

    // Key: Attribute, Value: RegenAttribute, RegenInterval
    private readonly Dictionary<BasicAttribute, Tuple<BasicAttribute, BasicAttribute>> regenStats;
    private readonly RegenClock regenClock = new();

    private readonly Dictionary<ActorState, float> stateSyncDistanceTracking;
    private long StateSyncTimeTracking { get; set; }
    private long StateSyncTrackingTick { get; set; }
    // Time since the client last synced its position.
    public long SinceStateSync => Field.FieldTick - StateSyncTrackingTick;

    private long LastCurrentBlockUpdateTick { get; set; }

    public FieldEntity? StandingOnBlock { get; set; }

    public Tombstone? Tombstone { get; set; }

    public DeathState DeathState {
        get => Value.Character.DeathState;
        set {
            if (value != Value.Character.DeathState) {
                Session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                    AccountId = Session.AccountId,
                    CharacterId = Session.CharacterId,
                    DeathState = (int) value,
                    Async = true,
                });
            }
            Value.Character.DeathState = value;
        }
    }

    #region DebugFlags
    private bool debugAi = false;
    public bool DebugSkills = false;
    // GM toggles (chat commands of the client's GM hotkeys); cleared when the account stops being a GM.
    // Kept on the session: a field entry makes a new FieldPlayer and the toggle has to stay on.
    public bool InvisibleFromNpc {
        get => Session.GmInvisibleFromNpc;
        set => Session.GmInvisibleFromNpc = value;
    }
    public bool DeathPower {
        get => Session.GmDeathPower;
        set => Session.GmDeathPower = value;
    }
    #endregion

    public int TagId = 1;
    public AdminPermissions AdminPermissions {
        get => Value.Account.AdminPermissions;
        set => Value.Account.AdminPermissions = value;
    }

    private readonly EventQueue scheduler;

    public FieldPlayer(GameSession session, Player player) : base(session.Field!, player.ObjectId, player, session.NpcMetadata) {
        Session = session;
        Animation = Session.Animation;

        regenStats = new Dictionary<BasicAttribute, Tuple<BasicAttribute, BasicAttribute>>();

        stateSyncDistanceTracking = new Dictionary<ActorState, float>();
        StateSyncTimeTracking = 0;
        StateSyncTrackingTick = Environment.TickCount64;

        scheduler = new EventQueue(Logger);
        scheduler.ScheduleRepeated(() => {
            session.ConditionUpdate(ConditionType.stay_map, codeLong: session.Field!.MapId);
        }, TimeSpan.FromSeconds(60), skipFirst: true);
        scheduler.Start();
    }

    protected override void Dispose(bool disposing) {
        scheduler.Stop();
    }

    public static implicit operator Player(FieldPlayer fieldPlayer) => fieldPlayer.Value;

    public bool InBattle {
        get => inBattle;
        set {
            if (value != inBattle) {
                inBattle = value;
                Session.Field?.Broadcast(SkillPacket.InBattle(this));
            }

            if (inBattle) {
                battleTick = Environment.TickCount64;
            }
        }
    }

    public bool DebugAi {
        get => debugAi;
        set {
            if (value) {
                Field.BroadcastAiType(Session);
            }

            debugAi = value;
        }
    }

    // The party buffs (PartyBuff) at the level the party members in this field give; none outside a party, alone, or
    // in a PvP field (the exp and meso buff is removed in one).
    private void UpdatePartyBuffs() {
        int partyId = Session.Party.Id;
        int others = partyId == 0 || Field.Metadata.Property.Type == MapType.Pvp ? 0
            : Field.Players.Values.Count(other => other != this && other.Session.Party.Id == partyId);
        short level = PartyBuff.Level(others);

        ConstantsTable constants = Session.ServerTableMetadata.ConstantsTable;
        foreach (int buffId in new[] { constants.partyBuffID0, constants.partyBuffID1 }) {
            if (buffId == 0) {
                continue;
            }
            short current = Buffs.EnumerateBuffs(buffId).Select(buff => buff.Level).DefaultIfEmpty((short) 0).Max();
            if (current == level) {
                continue;
            }
            if (current > 0) {
                Buffs.Remove(buffId, ObjectId);
            }
            if (level > 0) {
                Buffs.AddBuff(this, this, buffId, level, Field.FieldTick);
            }
        }
    }

    public override void Update(long tickCount) {
        base.Update(tickCount);
        healthSyncTick = tickCount;
        if (healthSync.Due(tickCount)) {
            SendHealth();
        }
        Session.GameEvent.Update(tickCount);
        scheduler.InvokeAll();

        if (Flag != PlayerObjectFlag.None && tickCount > flagTick) {
            Field.Broadcast(ProxyObjectPacket.UpdatePlayer(this, Flag));
            Flag = PlayerObjectFlag.None;
            flagTick = (long) (tickCount + TimeSpan.FromSeconds(2).TotalMilliseconds);
        }

        UpdateTimeConditions();

        if (tickCount >= partyBuffTick) {
            partyBuffTick = tickCount + PartyBuffIntervalMs;
            UpdatePartyBuffs();
        }

        if (Field.MapId == Constant.DefaultHomeMapId) {
            if (restExpTick == 0) {
                restExpTick = tickCount + RestExpRules.TickMs;
            } else if (tickCount >= restExpTick) {
                restExpTick = tickCount + RestExpRules.TickMs;
                Session.Exp.TickRestExp();
            }
        }

        if (IsDead) {
            if (Tombstone == null) {
                return;
            }
            if (Tombstone.HitsRemaining == 0) {
                Revive();
                return;
            }
            if (autoReviveDelay > 0) {
                autoReviveTick = tickCount + autoReviveDelay;
                autoReviveDelay = 0;
            } else if (autoReviveTick != 0 && tickCount >= autoReviveTick) {
                Revive();
            }
            return;
        }

        if (InBattle && tickCount - battleTick > Constants.UserBattleDurationTick) {
            InBattle = false;
        }

        UpdateStateSkill();

        if (IsDead) {
            return;
        }
        // Loops through each registered regen stat and applies regen
        var statsToRemove = new List<BasicAttribute>();
        foreach (BasicAttribute attribute in regenStats.Keys) {
            Stat stat = Stats.Values[attribute];
            Stat regen = Stats.Values[regenStats[attribute].Item1];
            Stat interval = Stats.Values[regenStats[attribute].Item2];

            if (stat.Current >= stat.Total) {
                // Removes stat from regen stats so it won't be listened for
                statsToRemove.Add(attribute);
                continue;
            }

            if (regenClock.TryTick(attribute, tickCount, interval.Current)) {
                switch (attribute) {
                    case BasicAttribute.Health:
                        RecoverHp((int) regen.Total);
                        continue;
                    case BasicAttribute.Spirit:
                        RecoverSp((int) regen.Total);
                        continue;
                    case BasicAttribute.Stamina:
                        RecoverStamina((int) regen.Total);
                        continue;
                }
                Session.Send(StatsPacket.Update(this, attribute));
            }
        }
        foreach (BasicAttribute attribute in statsToRemove) {
            regenStats.Remove(attribute);
        }
        CheckRegen();
        return;

        void UpdateStateSkill() {
            SkillRecord? stateSkill = ActiveSkills.StateSkill;
            if (stateSkill == null) {
                return;
            }

            if (stateSkill.StateNextTick > tickCount) {
                return;
            }

            if (stateSkill.Metadata.Property.State != State) {
                Field.Broadcast(SkillPacket.Cancel(stateSkill));
                ActiveSkills.StateSkill = null;
                return;
            }

            stateSkill.StateNextTick = tickCount + (int) TimeSpan.FromSeconds(Math.Max(0.01f, stateSkill.Motion.MotionProperty.SequenceSpeed)).TotalMilliseconds;
            if (!SkillCastConsume(stateSkill)) {
                ActiveSkills.StateSkill = null;
                Field.Broadcast(SkillPacket.Cancel(stateSkill));
            }
        }
    }

    // Play time counts once a minute for the whole session, across maps. Emotion time counts while the last state
    // sync holds the player in an emotion (sitting sends no further change), by the emotion's name.
    private void UpdateTimeConditions() {
        long now = Environment.TickCount64;
        int minutes = TrophyTimeRules.PlayMinutes(ref Session.PlayTimeTick, now);
        if (minutes > 0) {
            Session.ConditionUpdate(ConditionType.playtime, counter: minutes);
        }

        if (emotionSkillId == 0) {
            return;
        }
        long elapsed = now - emotionTick;
        emotionTick = now;
        int seconds = TrophyTimeRules.EmotionSeconds(ref emotionMs, elapsed);
        if (seconds > 0 && Field.SkillMetadata.TryGet(emotionSkillId, 1, out SkillMetadata? emote) && !string.IsNullOrEmpty(emote.Property.Emotion)) {
            Session.ConditionUpdate(ConditionType.emotiontime, counter: seconds, codeString: emote.Property.Emotion);
        }
    }

    // stay_cube is a time condition (the client groups it with playtime, stay_map and survive_cube): one second on
    // a cube, in any state. The code is the map, the target the cube's material (grass, snow, glass in the quests).
    internal static void ReportStayCube(GameSession session, FieldEntity? block) {
        MapAttribute? attribute = block switch {
            FieldMeshColliderEntity mesh => mesh.MapAttribute,
            FieldBoxColliderEntity { IsFluid: false } box => box.MapAttribute,
            _ => null,
        };
        if (attribute == null || session.Field == null) {
            return;
        }
        session.ConditionUpdate(ConditionType.stay_cube, codeLong: session.Field.MapId, targetString: attribute.Value.ToString());
    }

    public void OnStateSync(StateSync stateSync) {
        int syncEmotion = TrophyTimeRules.EmotionSkillId(stateSync);
        if (syncEmotion != emotionSkillId) {
            emotionSkillId = syncEmotion;
            emotionTick = Environment.TickCount64;
            emotionMs = 0;
        }

        if (Position != stateSync.Position) {
            Flag |= PlayerObjectFlag.Position;
        }

        float syncDistance = Vector3.Distance(Position, stateSync.Position); // distance between old player position and new state sync position
        long syncTick = Field.FieldTick - StateSyncTrackingTick; // time elapsed since last state sync
        StateSyncTrackingTick = Field.FieldTick;

        Position = stateSync.Position;
        Rotation = new Vector3(0, 0, stateSync.Rotation / 10f);

        State = stateSync.State;
        SubState = stateSync.SubState;
        StateActorId = AiTargetState.ActorOf(stateSync);

        if (stateSync.SyncNumber != int.MaxValue) {
            LastGroundPosition = stateSync.Position;
        }

        LastCurrentBlockUpdateTick += syncTick;
        if (LastCurrentBlockUpdateTick >= 1000) { // 1 second
            LastCurrentBlockUpdateTick = 0;
            Session.Field?.AccelerationStructure?.FindBlockUnderPlayer(Position, entity => {
                StandingOnBlock = entity;
                ReportStayCube(Session, entity);
            });
        }

        // Condition updates
        // Distance conditions are in increments of 1 meter, while time conditions are 1 second.
        switch (stateSync.State) {
            case ActorState.Fall:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.fall, codeLong: Value.Character.MapId);
                }
                break;
            case ActorState.SwimDash:
            case ActorState.Swim:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.swim, codeLong: Value.Character.MapId);
                }

                if (UpdateStateSyncTimeTracking()) {
                    Session.ConditionUpdate(ConditionType.swimtime, targetLong: Value.Character.MapId);
                }
                break;
            case ActorState.Walk:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.run, codeLong: Value.Character.MapId);
                }
                break;
            case ActorState.Crawl:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.crawl, codeLong: Value.Character.MapId);
                }
                break;
            case ActorState.Glide:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.glide, codeLong: Value.Character.MapId);
                }
                break;
            case ActorState.Climb:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.climb, codeLong: Value.Character.MapId);
                }
                break;
            case ActorState.Rope:
                if (UpdateStateSyncTimeTracking()) {
                    Session.ConditionUpdate(ConditionType.ropetime, targetLong: Value.Character.MapId);
                }
                break;
            case ActorState.Ladder:
                if (UpdateStateSyncTimeTracking()) {
                    Session.ConditionUpdate(ConditionType.laddertime, targetLong: Value.Character.MapId);
                }
                break;
            case ActorState.Hold:
                if (UpdateStateSyncTimeTracking()) {
                    Session.ConditionUpdate(ConditionType.holdtime, targetLong: Value.Character.MapId);
                }
                break;
            case ActorState.Ride:
                if (UpdateStateSyncTracking()) {
                    Session.ConditionUpdate(ConditionType.riding, codeLong: Value.Character.MapId);
                }
                break;
                // TODO: Any more condition states?
        }

        Field.EnsurePlayerPosition(this);
        return;

        bool UpdateStateSyncTimeTracking() {
            StateSyncTimeTracking += syncTick;
            if (StateSyncTimeTracking >= 1000) {
                StateSyncTimeTracking = 0;
                return true;
            }
            return false;
        }

        bool UpdateStateSyncTracking() {
            if (stateSyncDistanceTracking.TryGetValue(state, out float totalDistance)) {
                totalDistance += syncDistance;
                // 150f = BLOCK_SIZE = 1 meter
                if (totalDistance >= 150F) {
                    stateSyncDistanceTracking[state] = 0f;
                    return true;
                }
                stateSyncDistanceTracking[state] = totalDistance;
                return false;
            }
            stateSyncDistanceTracking[state] = syncDistance;
            return false;
        }
    }

    // A dead player takes no hits, nor does one inside the window after a revival.
    public static bool TakesDamage(bool isDead, long tick, long revivalInvincibleUntil) => !isDead && tick >= revivalInvincibleUntil;

    public override void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        if (!TakesDamage(IsDead, Field.FieldTick, revivalInvincibleUntil)) {
            return;
        }
        long healthBefore = Stats.Values[BasicAttribute.Health].Current;
        base.ApplyDamage(caster, damage, attack);
        if (Stats.Values[BasicAttribute.Health].Current != healthBefore) {
            // The health a hit takes goes to the world like any other health change, so the party frames of
            // members in other fields follow it (only heals, regen and non-skill damage were sent). Hits in a
            // fight are many: the first goes at once, the rest are held and the latest health goes from Update
            // when the interval ends; the hit that takes the last health goes at once.
            if (healthSync.Changed(healthSyncTick, force: Stats.Values[BasicAttribute.Health].Current <= 0)) {
                SendHealth();
            }
        }
        if (caster is not FieldPlayer) {
            // A hit by a monster's or the map's skill counts for skill_damage_npc with the skill id as the code
            // (70400009 "Get attacked by a seedling at the Maple Arcade" 49230005, 21233002 "Get trapped in the
            // $npc:23000018$'s water prison" 99999981).
            Session.ConditionUpdate(ConditionType.skill_damage_npc, codeLong: damage.SkillMetadata.Id);
            // The hit that takes the last health also counts for skill_die with the same code (21233007 "Deadly
            // Whirlwind" 70000500, 21233011 "Spinning Laser" 50100394/50100405/50100406).
            if (KilledByHit(healthBefore, Stats.Values[BasicAttribute.Health].Current)) {
                Session.ConditionUpdate(ConditionType.skill_die, codeLong: damage.SkillMetadata.Id);
            }
        }
    }

    private void SendHealth() {
        Session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
            AccountId = Session.AccountId,
            CharacterId = Session.CharacterId,
            Health = new HealthUpdate {
                CurrentHp = Stats.Values[BasicAttribute.Health].Current,
                TotalHp = Stats.Values[BasicAttribute.Health].Total,
            },
            Async = true,
        });
    }

    // A hit kills when it takes a living player's health to 0; a later hit in the same tick finds no health left.
    public static bool KilledByHit(long healthBefore, long healthAfter) => healthBefore > 0 && healthAfter <= 0;

    protected override void OnDeath() {
        if (Session.Field is null) return;
        Field.Broadcast(SetCraftModePacket.Stop(ObjectId));

        Session.HeldCube = null;
        InBattle = false;
        // The hits a tombstone takes grow with deaths in a row (each within timeResetDead of the one before),
        // not with the death penalty count: KMS2 gives 5, 10, 15 to a character below the penalty level.
        Tombstone = new Tombstone(this, DeathStreaks.Count(Value.Character.Id, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Constants.timeResetDead));
        if (RideShare.DismountsOnDeath(Session.Ride.RideType)) {
            Session.Ride.Dismount(RideOffType.Dead);
        }
        bool darkTomb = Session.Field.Metadata.Property.OnlyDarkTomb || Session.Config.DeathCount > 0;
        Field.Broadcast(DeadUserPacket.Dead(ObjectId, darkTomb));

        Buffs.OnDeath();
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.CountMissions(this, mission => mission.AddDeath());
        }

        int autoReviveMs = AutoReviveDelayMs(Session.Field.Metadata.Property);
        if (autoReviveMs > 0) {
            autoReviveDelay = autoReviveMs;
            autoReviveTick = 0;
            if (Session.Field.Metadata.Property.AutoRevivalType == AutoReviveType.Countdown) {
                Session.Send(AutoRevivePacket.Countdown(autoReviveMs));
            }
        }
    }

    // The field xml's autoRevivalTime is in seconds: the client only tests it for > 0 (CFieldData::HasAutoRevivalType1/2,
    // fielddata.cpp:827), and the field data holds only 1, 3, 8, 9 and 20. The packet's int is in milliseconds: the
    // countdown dialog adds it to the tick count and shows it divided by 1000 (uiautorevivecountdowndialog.cpp:78).
    public static int AutoReviveDelayMs(MapMetadataProperty property) {
        if (property.AutoRevivalType == AutoReviveType.None || property.AutoRevivalTime <= 0) {
            return 0;
        }
        return property.AutoRevivalTime * 1000;
    }

    /// <summary>
    /// Revives the player from death state.
    /// </summary>
    /// <param name="instant">If true, performs an instant revival without moving to spawn point.</param>
    public bool Revive(bool instant = false) {
        // Check if player can be revived
        if (!IsDead || Tombstone == null) {
            return false;
        }

        autoReviveDelay = 0;
        autoReviveTick = 0;

        IsDead = false;
        // OnDeath took every buff without keepOnDeath, the lasting ones too: passive skills, gear and set
        // effects, character abilities, prestige, lapenshards, premium club, guild and insignia. A field entry
        // gives them again; a revive must too, or the player keeps the lower stats (a character ability's +31 HP)
        // until the next map. Before the HP fill, so the player comes back at the full total.
        if (RevivalRules.RestoresLastingBuffs(instant, Field.Metadata.Property.RevivalReturnId, Field.Metadata.Id)) {
            Session.Config.Skill.UpdatePassiveBuffs();
            Buffs.LoadFieldBuffs();
            Session.Stats.Refresh();
        }
        RecoverHp((int) Stats.Values[BasicAttribute.Health].Total);

        // Apply death penalty if field requires it
        if (Field.Metadata.Property.DeathPenalty) {
            Session.Config.UpdateDeathPenalty(Field.FieldTick + Constants.UserRevivalPaneltyTick);
        }

        // Update revival condition
        Session.ConditionUpdate(ConditionType.revival);

        // Mark tombstone as cleared
        Tombstone.Clear();

        // Handle revival return to different map
        if (!instant && Field.Metadata.Property.RevivalReturnId != 0 && Field.Metadata.Property.RevivalReturnId != Field.Metadata.Id) {
            Session.Send(Session.PrepareField(Field.Metadata.Property.RevivalReturnId)
                ? FieldEnterPacket.Request(Session.Player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
            return true;
        }

        // Send revival packets
        Session.Send(StatsPacket.Init(this));
        if (instant) {
            Session.Send(RevivalPacket.RevivalCount(Session.Config.InstantReviveCount));
        }
        Field.Broadcast(RevivalPacket.Revive(ObjectId));
        revivalInvincibleUntil = Field.FieldTick + Session.ServerTableMetadata.ConstantsTable.UserRevivalInvincibleTick;

        // Move to spawn point if not instant revival
        if (!instant) {
            if (Field.TryGetPlayerSpawn(-1, out FieldPlayerSpawnPoint? point)) {
                // Same placement as entering the field at a spawn point: 25 above it, and the packet
                // writer adds those 25. MoveToPosition drops the move without a word when the point
                // itself is not on the navigation mesh. The stored position follows what was sent, so
                // the server does not keep the player at the death spot until the next client sync.
                Session.Send(PortalPacket.MoveByPortal(this, point.Position, point.Rotation));
                Position = point.Position + new Vector3(0, 0, 25);
                Rotation = point.Rotation;
            } else {
                Logger.Debug("Revive of {CharacterId} in map {MapId}: no enabled player spawn point, not moved", Value.Character.Id, Field.MapId);
            }
        }

        Buffs.UpdateEnabled();
        CheckRegen();
        return true;
    }

    /// <summary>
    /// Adds health to player, and sends update packet.
    /// </summary>
    /// <param name="amount"></param>
    public void RecoverHp(int amount) {
        if (amount <= 0) {
            return;
        }

        Stat stat = Stats.Values[BasicAttribute.Health];
        if (stat.Current < stat.Total) {
            stat.Add(amount);
            Session.Send(StatsPacket.Update(this, BasicAttribute.Health));
        }

        healthSync.Sent(healthSyncTick);
        SendHealth();
    }

    /// <summary>
    /// Consumes health and starts regen if not already started.
    /// </summary>
    /// <param name="amount"></param>
    public void ConsumeHp(int amount) {
        if (amount <= 0) {
            return;
        }

        Stat stat = Stats.Values[BasicAttribute.Health];
        stat.Add(-amount);
        DelayHpRegen();
        Session.Send(StatsPacket.Update(this, BasicAttribute.Health));

        healthSync.Sent(healthSyncTick);
        SendHealth();
        Field.Broadcast(StatsPacket.Update(this, BasicAttribute.Health));
    }

    /// <summary>
    /// Adds spirit to player, and sends update packet.
    /// </summary>
    /// <param name="amount"></param>
    public void RecoverSp(int amount) {
        if (amount <= 0) {
            return;
        }

        Stat stat = Stats.Values[BasicAttribute.Spirit];
        if (stat.Current < stat.Total) {
            stat.Add(amount);
            Session.Send(StatsPacket.Update(this, BasicAttribute.Spirit));
        }
    }

    /// <summary>
    /// Consumes spirit and starts regen if not already started.
    /// </summary>
    /// <param name="amount"></param>
    public void ConsumeSp(int amount) {
        if (amount <= 0) {
            return;
        }

        Stats.Values[BasicAttribute.Spirit].Add(-amount);
        Field.Broadcast(StatsPacket.Update(this, BasicAttribute.Spirit));
    }

    /// <summary>
    /// Adds stamina to player, and sends update packet.
    /// </summary>
    /// <param name="amount"></param>
    public void RecoverStamina(int amount) {
        if (amount <= 0) {
            return;
        }

        Stat stat = Stats.Values[BasicAttribute.Stamina];
        if (stat.Current < stat.Total) {
            Stats.Values[BasicAttribute.Stamina].Add(amount);
            Session.Send(StatsPacket.Update(this, BasicAttribute.Stamina));
        }
    }

    /// <summary>
    /// Consumes stamina. The first refill comes one StaminaRegenInterval after the use, with no wait tick on top
    /// (chosen here: RecoveryEPWaitTick is not applied to a use). See <see cref="RegenClock"/>.
    /// </summary>
    /// <param name="amount">The amount</param>
    /// <param name="noRegen">If regen shouldn't be started</param>
    public void ConsumeStamina(int amount, bool noRegen = false) {
        if (amount <= 0) {
            return;
        }

        Stats.Values[BasicAttribute.Stamina].Add(-amount);
        DelayStaminaRegen();
        Field.Broadcast(StatsPacket.Update(this, BasicAttribute.Stamina));
    }

    /// <summary>
    /// The player used stamina: the next stamina regen tick comes one StaminaRegenInterval later.
    /// </summary>
    public void DelayStaminaRegen() {
        if (!IsDead) {
            regenClock.Hit(BasicAttribute.Stamina, Field.FieldTick, 0);
        }
    }

    /// <summary>
    /// The player lost health (a skill hit, a DoT, a fall, a skill's HP cost): the next health regen tick comes
    /// RecoveryHPWaitTick plus one HpRegenInterval later. See <see cref="RegenClock"/>.
    /// </summary>
    public void DelayHpRegen() {
        if (!IsDead) {
            regenClock.Hit(BasicAttribute.Health, Field.FieldTick, Constants.RecoveryHPWaitTick);
        }
    }

    protected override void OnDamageReceived(IActor caster, long amount) {
        if (amount > 0) {
            DelayHpRegen();
        }
    }

    public void CheckRegen() {
        // Health
        Stat health = Stats.Values[BasicAttribute.Health];
        if (health.Current < health.Total && !regenStats.ContainsKey(BasicAttribute.Health)) {
            regenStats.Add(BasicAttribute.Health, new Tuple<BasicAttribute, BasicAttribute>(BasicAttribute.HpRegen, BasicAttribute.HpRegenInterval));
        }

        // Spirit
        Stat spirit = Stats.Values[BasicAttribute.Spirit];
        if (spirit.Current < spirit.Total && !regenStats.ContainsKey(BasicAttribute.Spirit)) {
            regenStats.Add(BasicAttribute.Spirit, new Tuple<BasicAttribute, BasicAttribute>(BasicAttribute.SpRegen, BasicAttribute.SpRegenInterval));
        }

        // Stamina
        Stat stamina = Stats.Values[BasicAttribute.Stamina];
        if (stamina.Current < stamina.Total && !regenStats.ContainsKey(BasicAttribute.Stamina)) {
            regenStats.Add(BasicAttribute.Stamina, new Tuple<BasicAttribute, BasicAttribute>(BasicAttribute.StaminaRegen, BasicAttribute.StaminaRegenInterval));
        }
    }

    public override void KeyframeEvent(string keyName) { }

    public void MoveToPosition(Vector3 position, Vector3 rotation) {
        if (!Field.ValidPosition(position)) {
            return;
        }

        Session.Send(PortalPacket.MoveByPortal(this, position, rotation));
    }

    public void MoveToPortal(FieldPortal portal) {
        if (!Field.ValidPosition(portal.Position)) {
            return;
        }

        Session.Send(PortalPacket.MoveByPortal(this, portal.Position, portal.Rotation));
    }

    public void FallDamage(float distance) {
        double distanceScalingFactor = 0.04813; // base distance scaling factor
        double hpRatioExponent = 1.087; // HP ratio exponent for diminishing returns
        double currentHp = Stats.Values[BasicAttribute.Health].Current;
        double maxHp = Stats.Values[BasicAttribute.Health].Total;
        double distanceFactor = distanceScalingFactor * Math.Exp(0.0046 * distance);
        double hpRatio = currentHp / maxHp;
        double hpScaling = Math.Pow(hpRatio, hpRatioExponent);

        double damageD = currentHp * distanceFactor * hpScaling;
        damageD = Math.Min(currentHp * 0.25, damageD);
        int damage = (int) damageD;
        if (damage > 0) {
            ConsumeHp(damage);
            Field.Broadcast(StatsPacket.Update(this, BasicAttribute.Health));
            Session.Send(FallDamagePacket.FallDamage(ObjectId, damage));
            Session.ConditionUpdate(ConditionType.fall_damage, targetLong: damage);
            if (!IsDead) {
                Session.ConditionUpdate(ConditionType.fall_survive);
            } else {
                Session.ConditionUpdate(ConditionType.fall_die);
            }
        }
    }

    // A player's cast is sent to the field once, by the handler, after the rest of the request is read and the
    // cost is paid: KMS2 sends one SkillUse per cast.
    public override SkillRecord? CastSkill(int id, short level, long uid, int castTick, in Vector3 position = default, in Vector3 direction = default, in Vector3 rotation = default, float rotateZ = 0f, byte motionPoint = 0) {
        return NewCast(id, level, uid, castTick, position, direction, rotation, rotateZ, motionPoint);
    }

    public override bool SkillCastConsume(SkillRecord record) {
        if (!base.SkillCastConsume(record)) {
            return false;
        }

        SkillMetadata metadata = record.Metadata;

        // Every cost is checked before any is paid, so a refused cast pays nothing.
        if (metadata.Data.Consume.Meso > 0 && Session.Currency.Meso < metadata.Data.Consume.Meso) {
            Logger.Error("Not enough meso to cast skill: {SkillId},{Level}", metadata.Id, metadata.Level);
            return false;
        }
        if (!CanPayCastStats(metadata, out int spiritCost, out int staminaCost)) {
            return false;
        }
        // HpRate is a negative value in decimal form (e.g., -0.01 for 1%)
        long hp = Stats.Values[BasicAttribute.Health].Current;
        int hpToConsume = SkillCastRules.HpCost(hp, metadata.Data.Consume.HpRate);
        if (SkillCastRules.HpCostRefused(hp, hpToConsume)) {
            Logger.Error("Not enough health to cast skill: {SkillId},{Level}", metadata.Id, metadata.Level);
            return false;
        }

        bool consumesItem = SkillCastRules.ConsumesItem(metadata.Data.Consume.UseItem, record.ItemConsumed);
        if (SkillCastRules.ItemMissing(consumesItem, record.ItemUid, Session.Pet != null && Session.Pet.Contains(record.ItemUid),
                Session.Item.Inventory.Get(record.ItemUid) != null)) {
            Logger.Error("Invalid item uid for skill: {SkillId},{Level}", metadata.Id, metadata.Level);
            return false;
        }

        if (metadata.Data.Consume.Meso > 0) {
            Session.Currency.Meso -= metadata.Data.Consume.Meso;
        }
        PayCastStats(spiritCost, staminaCost);
        if (hpToConsume > 0) {
            ConsumeHp(hpToConsume);
        }

        // One item per cast: a tick sync of the same cast pays the other costs again, not the item.
        if (consumesItem) {
            // The uid may name an item in the summoned pet's bag: the client looks there first.
            if (Session.Pet != null && Session.Pet.Contains(record.ItemUid)) {
                if (!Session.Pet.Consume(record.ItemUid, 1)) {
                    Logger.Error("Failed to consume pet bag item {ItemUid} for skill: {SkillId},{Level}", record.ItemUid, metadata.Id, metadata.Level);
                    return false;
                }
                record.ItemConsumed = true;
                return true;
            }

            Item? item = Session.Item.Inventory.Get(record.ItemUid);
            if (item == null) {
                Logger.Error("Invalid item uid for skill: {SkillId},{Level}", metadata.Id, metadata.Level);
                return false;
            }

            if (!Session.Item.Inventory.Consume(item.Uid, 1)) {
                Logger.Error("Failed to consume item {itemId} for skill: {SkillId},{Level}", item.Id, metadata.Id, metadata.Level);
                return false;
            }
            record.ItemConsumed = true;
        }

        return true;
    }

    // The spirit and stamina a held skill pays again at a tick sync of its cast (SkillCastRules.SyncPays).
    public bool SkillSyncConsume(SkillRecord record) {
        if (!CanPayCastStats(record.Metadata, out int spiritCost, out int staminaCost)) {
            return false;
        }
        PayCastStats(spiritCost, staminaCost);
        return true;
    }

    private bool CanPayCastStats(SkillMetadata metadata, out int spiritCost, out int staminaCost) {
        spiritCost = 0;
        staminaCost = 0;
        if (metadata.Data.Consume.Stat.TryGetValue(BasicAttribute.Spirit, out long spirit)) {
            (int invokeValue, float invokeRate) = Buffs.GetInvokeValues(InvokeEffectType.ReduceSpiritCost, metadata.Id, metadata.Property.SkillGroup);
            spiritCost = SkillCastRules.SpiritCost(spirit, invokeValue, invokeRate, Buffs.ConsumeSpRate);
            if (Stats.Values[BasicAttribute.Spirit].Current < spiritCost) {
                Logger.Error("Not enough spirit to cast skill: {SkillId},{Level}", metadata.Id, metadata.Level);
                return false;
            }
        }
        if (metadata.Data.Consume.Stat.TryGetValue(BasicAttribute.Stamina, out long stamina)) {
            staminaCost = (int) stamina;
            if (Stats.Values[BasicAttribute.Stamina].Current < staminaCost) {
                Logger.Error("Not enough stamina to cast skill: {SkillId},{Level}", metadata.Id, metadata.Level);
                return false;
            }
        }
        return true;
    }

    private void PayCastStats(int spiritCost, int staminaCost) {
        if (spiritCost > 0) {
            ConsumeSp(spiritCost);
        }
        if (staminaCost > 0) {
            ConsumeStamina(staminaCost);
        }
    }

    public override string ToString() {
        return $"FieldPlayer: {Value.Character.Name} ({ObjectId}) at {Position} in {Field.Metadata.Name} ({Field.Metadata.Id})" +
               $"\n  State: {State}, SubState: {SubState}, Flag: {Flag}, IsDead: {IsDead}" +
               $"\n  Position: {Position}, Rotation: {Rotation}, LastGroundPosition: {LastGroundPosition}" +
               $"\n  AdminPermissions: {AdminPermissions}, TagId: {TagId}" +
               $"\n  DebugAi: {DebugAi}, DebugSkills: {DebugSkills}";
    }
}
