using System.Numerics;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void ChangeMonster(int removeSpawnId, int addSpawnId) {
        DebugLog("[ChangeMonster] removeSpawnId:{RemoveId}, addSpawnId:{AddId}", removeSpawnId, addSpawnId);
        foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
            if (fieldNpc.SpawnPointId == removeSpawnId) {
                Field.RemoveNpc(fieldNpc.ObjectId);
            }
        }

        SpawnMonster(addSpawnId);
    }

    public void SpawnMonster(int[] spawnIds, bool autoTarget, int delay) {
        DebugLog("[CreateMonster] spawnIds:{SpawnIds}, autoTarget:{AutoTarget}, delay:{Delay}", string.Join(", ", spawnIds), autoTarget, delay);
        foreach (int spawnId in spawnIds) {
            if (delay > 0) {
                Events.Schedule(() => SpawnNpc(spawnId, autoTarget), TimeSpan.FromMilliseconds(delay));
            } else {
                SpawnNpc(spawnId, autoTarget);
            }
        }
    }

    public void DestroyMonster(int[] spawnIds, bool arg2) {
        DebugLog("[DestroyMonster] spawnIds:{SpawnIds}, Arg2:{Arg2}", string.Join(", ", spawnIds), arg2);
        if (spawnIds.Contains(-1)) {
            foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
                Field.RemoveNpc(fieldNpc.ObjectId);
            }
            return;
        }

        foreach (int spawnPointId in spawnIds) {
            foreach (FieldNpc fieldNpc in Field.EnumerateNpcs()) {
                if (fieldNpc.SpawnPointId == spawnPointId) {
                    Field.RemoveNpc(fieldNpc.ObjectId);
                }
            }
        }
    }

    private void SpawnMonster(int spawnId) {
        DebugLog("[SpawnMonster] spawnId:{SpawnId}", spawnId);
        SpawnNpc(spawnId);
    }

    // Turns the npcs of each spawn point back to the rotation the map gives that spawn point: 51000004 turns Pink Bean
    // with SetNpcRotation (270, 180, 90) and then calls InitNpcRotation on the same spawn to face it forward again.
    public void InitNpcRotation(int[] spawnIds) {
        DebugLog("[InitNpcRotation] spawnIds:{SpawnIds}", string.Join(", ", spawnIds));
        foreach (int spawnId in spawnIds) {
            Vector3? rotation = SpawnRotation(Field.Entities.EventNpcSpawns, Field.Entities.NpcSpawns, spawnId);
            if (rotation == null) {
                continue;
            }
            foreach (FieldNpc npc in Field.EnumerateNpcs()) {
                if (npc.SpawnPointId == spawnId) {
                    npc.Rotation = rotation.Value;
                }
            }
        }
    }

    public static Vector3? SpawnRotation(IReadOnlyDictionary<int, EventSpawnPointNPC> eventSpawns, IEnumerable<SpawnPointNPC> npcSpawns, int spawnId) =>
        FindSpawnPoint(eventSpawns, npcSpawns, spawnId)?.Rotation;

    // The shipped scripts' own comments give the rule: once the field's monster count passes
    // limitCount, spawning stops; limitCount 0 turns the limit off.
    public void LimitSpawnNpcCount(int limitCount, string desc) {
        DebugLog("[LimitSpawnNpcCount] limitCount:{Count}", limitCount);
        Field.TriggerMobLimit = Math.Max(0, limitCount);
    }

    public static bool CanSpawnUnderLimit(int limit, int mobCount) => limit <= 0 || mobCount < limit;

    public void MoveNpc(int spawnId, string patrolName) {
        DebugLog("[MoveNpc] spawnId:{SpawnId} patrolName:{PatrolName}", spawnId, patrolName);

        List<FieldNpc> fieldNpcs = Field.EnumerateNpcs().Where(npc => npc.SpawnPointId == spawnId).ToList();
        if (fieldNpcs.Count == 0) {
            return;
        }

        MS2PatrolData? patrolData = Field.Entities.Patrols.FirstOrDefault(patrol => patrol.Name == patrolName);

        if (patrolData is null) {
            return;
        }

        foreach (FieldNpc fieldNpc in fieldNpcs) {
            fieldNpc.SetPatrolData(patrolData, byScript: true);
        }
    }

    public void MoveNpcToPos(int spawnId, Vector3 position, Vector3 rotation) {
        DebugLog("[MoveNpcToPos] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Position = position;
                npc.Rotation = rotation;
            }
        }
    }

    public void NpcRemoveAdditionalEffect(int spawnId, int additionalEffectId) {
        DebugLog("[NpcRemoveAdditionalEffect] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Buffs.Remove(additionalEffectId, npc.ObjectId);
            }
        }
    }

    // Each npc of that id in the box goes on the path. spawn_id names where the npc came from (every use in the
    // data says interactObject: the npc an object's use spawned) and picks nothing more. The survival maps run it
    // every 200 ms on their boat docks: a boat already on the path keeps its place on it.
    public void NpcToPatrolInBox(int boxId, int npcId, string spawnId, string patrolName) {
        DebugLog("[NpcToPatrolInBox] boxId:{BoxId}, npcId:{NpcId}, spawnId:{SpawnId}, patrolName:{PatrolName}", boxId, npcId, spawnId, patrolName);
        if (boxId == 0) {
            return;
        }
        MS2PatrolData? patrolData = Field.Entities.Patrols.FirstOrDefault(patrol => patrol.Name == patrolName);
        if (patrolData is null) {
            return;
        }

        foreach (FieldNpc npc in NpcsInBox(boxId).ToList()) {
            if (PatrolInBoxSets(npc.Value.Metadata.Id, npc.Patrol?.Name, npcId, patrolName)) {
                npc.SetPatrolData(patrolData, byScript: true);
            }
        }
    }

    public static bool PatrolInBoxSets(int npcIdInBox, string? currentPatrol, int npcId, string patrolName) {
        return npcIdInBox == npcId && currentPatrol != patrolName;
    }

    public void SetAiExtraData(string key, int value, bool isModify, int boxId) {
        DebugLog("[SetAiExtraData] key:{Key}, value:{Value}, isModify:{IsModify}, boxId:{BoxId}", key, value, isModify, boxId);
        var npcs = boxId != 0 ? NpcsInBox(boxId) : Field.EnumerateNpcs();
        foreach (FieldNpc npc in npcs) {
            // Assumed that we increment the current by the value if isModify is true
            if (isModify) {
                if (npc.AiExtraData.TryGetValue(key, out int oldValue)) {
                    npc.AiExtraData[key] = oldValue + value;
                    continue;
                }
            }
            npc.AiExtraData[key] = value;
        }
    }

    public void SetDialogue(int type, int spawnId, string script, int delay, int arg5, Align align) {
        DebugLog("[SetDialogue] type:{Type}, spawnId:{SpawnId}, script:{Script}, delay:{Delay}, arg5:{Arg5}, align:{Align}",
            type, spawnId, script, delay, arg5, align);

        // arg5 is the balloon's start delay in seconds (KR 02000013_bf im_542: a second balloon of the same npc with
        // arg5 2 after a first of time 2); the client keeps each actor's balloons with their own start, now + delay
        // (UICutSceneCinematic 0x141909eb0). The type 2 talk has no delay field.
        if (spawnId == 0) {
            SendOwnBalloon(script, delay * 1000, arg5 * 1000);
            return;
        }

        if (type == 1) {
            var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
            if (npc == null) {
                return;
            }

            Broadcast(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, npc.ObjectId, script, delay * 1000, arg5 * 1000));
            return;
        }

        // Type 2 names an npc id: the client shows that npc's name and loads the illustration <ILLUST_PATH><name>.dds
        // (UICutSceneCinematic FUN_141908750, FUN_140cc2c70), and quest/illust holds one named by npc id for many npcs.
        Broadcast(CinematicPacket.Talk(spawnId, spawnId.ToString(), script, delay * 1000, align));
    }

    public void SetNpcDuelHpBar(bool isOpen, int spawnId, int durationTick, int npcHpStep) {
        DebugLog("[SetNpcDuelHpBar] isOpen:{IsOpen}, spawnId:{SpawnId}, durationTick:{Duration}, npcHpStep:{HpStep}", isOpen, spawnId, durationTick, npcHpStep);
        // The client's close reads only the flag (Trigger 21, CTriggerService::OnNpcDuelHpBar), and the KR scripts
        // close the bar after the duel npc died or was destroyed, so the close needs no npc.
        if (!isOpen) {
            Broadcast(TriggerPacket.DuelHpBar(false));
            return;
        }
        IActor? actor = Field.GetActorsBySpawnId(spawnId).FirstOrDefault();
        if (actor is null) {
            return;
        }

        Broadcast(TriggerPacket.DuelHpBar(isOpen, actor.ObjectId, durationTick, npcHpStep));
    }

    public void SetNpcEmotionLoop(int spawnId, string sequenceName, float duration) {
        DebugLog("[SetNpcEmotionLoop] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, duration);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.Animate(sequenceName, duration);
    }

    public void SetNpcEmotionSequence(int spawnId, string sequenceName, int durationTick) {
        DebugLog("[SetNpcEmotionSequence] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, durationTick);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.AnimateSequence(sequenceName);
    }

    public void SetNpcRotation(int spawnId, float rotation) {
        DebugLog("[SetNpcRotation] spawnId:{SpawnId}, rotation:{Rotation}", spawnId, rotation);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Rotation = npc.Rotation with {
                    Z = rotation,
                };
            }
        }
    }

    public void SpawnNpcRange(int[] spawnIds, bool isAutoTargeting, int randomPickCount, int score) {
        DebugLog("[SpawnNpcRange] spawnIds:{SpawnIds}, isAutoTargeting:{AutoTarget}, randomPickCount:{RandomCount}, score:{Score}",
            string.Join(", ", spawnIds), isAutoTargeting, randomPickCount, score);

        // Each npc carries the score: its death adds it to the open board (FieldManager.AddScoreBoardScore).
        foreach (int spawnId in TriggerRandomPick.Pick(spawnIds, randomPickCount, Random.Shared)) {
            // is_auto_targeting plays the point's spawn animation, as spawn_monster's auto_target does.
            SpawnNpc(spawnId, isAutoTargeting, score);
        }
    }

    #region Conditions
    public bool CheckNpcAdditionalEffect(int spawnId, int additionalEffectId, int level, bool negate) {
        DebugLog("[CheckNpcAdditionalEffect] spawnId:{SpawnId}, additionalEffectId:{EffectId}, level:{Level}", spawnId, additionalEffectId, level);
        IEnumerable<IActor> actors = Field.GetActorsBySpawnId(spawnId);
        bool hasAnyBuff = false;
        foreach (IActor actor in actors) {
            if (actor is not FieldNpc) {
                continue;
            }

            if (!actor.Buffs.HasBuff(additionalEffectId, (short) level)) continue;

            hasAnyBuff = true;
            break;
        }

        return negate ? !hasAnyBuff : hasAnyBuff;
    }

    // CheckNpcSlaveCount: a monster from the spawn point has exactly count live summons of summon_group (all of its
    // summons when 0), as the AI's SlaveCount condition counts them.
    public bool CheckNpcSlaveCount(int spawnPointId, int count, int summonGroup) {
        DebugLog("[CheckNpcSlaveCount] spawnPointId:{SpawnId}, count:{Count}, summonGroup:{Group}", spawnPointId, count, summonGroup);
        int? group = summonGroup > 0 ? summonGroup : null;
        return Field.GetActorsBySpawnId(spawnPointId).OfType<FieldNpc>()
            .Any(npc => !npc.IsDead && AiSummon.SlaveCountPasses(count, npc.SlaveCount(group)));
    }

    public bool MonsterDead(int[] spawnIds, bool autoTarget, bool negate) {
        DebugLog("[MonsterDead] spawnIds:{SpawnIds}, arg2:{Arg2}, negate:{Negate}", string.Join(", ", spawnIds), autoTarget, negate);
        IEnumerable<FieldNpc> npcs = Field.Npcs.Values.Where(npc => !NotWaitedOn(Field.MapId, npc.SpawnPointId));
        IEnumerable<FieldNpc> matchingMobs = OfSpawnPoints(npcs, Field.Mobs.Values, npc => npc.SpawnPointId, spawnIds);
        return AllDead(matchingMobs.Select(mob => mob.IsDead), negate);
    }

    // Friendly npcs that monster_dead does not wait on, as before friendly npcs were looked at: nothing on their
    // map kills them, so the script would stall. No monster can walk to them on the navmesh and they cast nothing
    // that ends themselves: 02000213 starts its first scene on the death of spawn point 201 (an event npc on a
    // ledge above the monsters), and 02000349 and 52000076 bring up their fog on the death of spawn point 1098 (a
    // dummy spawned when the escort reaches the box). 52010038's core dummy (2097) is not here: its death is the
    // fail, and the script goes on with the timer while it stands.
    private static readonly HashSet<(int MapId, int SpawnPointId)> UnkilledFriendlies = [
        (02000213, 201),
        (02000349, 1098),
        (52000076, 1098),
    ];

    public static bool NotWaitedOn(int mapId, int spawnPointId) => UnkilledFriendlies.Contains((mapId, spawnPointId));

    // The npcs and the monsters of the listed spawn points. A spawn point a script waits on can hold a friendly
    // npc: 02000066 fails its waves on monster_dead spawn_ids="99", the friendly barrier the players guard, and
    // starts its helpers' talk on monster_in_combat spawn_ids="5001", a friendly npc as well.
    public static IEnumerable<T> OfSpawnPoints<T>(IEnumerable<T> npcs, IEnumerable<T> mobs, Func<T, int> spawnPointId, int[] spawnIds) =>
        npcs.Concat(mobs).Where(npc => spawnIds.Contains(spawnPointId(npc)));

    // Either no mobs were found or they are all dead; '!monster_dead' (negate) is the opposite.
    public static bool AllDead(IEnumerable<bool> isDead, bool negate) => isDead.All(dead => dead) != negate;

    public bool MonsterInCombat(int[] spawnIds, bool negate) {
        DebugLog("[MonsterInCombat] spawnIds:{SpawnIds}", string.Join(", ", spawnIds));
        return AnyInCombat(Field.Npcs.Values.Concat(Field.Mobs.Values).Select(mob => (mob.SpawnPointId, mob.BattleState.InBattle)), spawnIds, negate);
    }

    // True when a monster of one of the spawn points is in battle; negate is the opposite. Every shipped use names
    // one spawn point (02000066 spawn_ids="5001", 02000313 "2002"), so the monsters to look at are those with a
    // listed spawn point id.
    public static bool AnyInCombat(IEnumerable<(int SpawnPointId, bool InBattle)> mobs, int[] spawnIds, bool negate) {
        return mobs.Any(mob => mob.InBattle && spawnIds.Contains(mob.SpawnPointId)) != negate;
    }

    public bool NpcDetected(int boxId, int[] spawnIds, bool negate) {
        DebugLog("[NpcDetected] boxId:{BoxId}, spawnIds:{SpawnIds}", boxId, string.Join(", ", spawnIds));
        return AnyDetected(NpcsInBox(boxId).Select(npc => npc.SpawnPointId), spawnIds, negate);
    }

    // True when an npc in the box is of one of the spawn points (no spawn point named: any npc in the box);
    // negate turns the answer around in both cases.
    public static bool AnyDetected(IEnumerable<int> spawnPointIdsInBox, int[] spawnIds, bool negate) {
        bool any = spawnIds.Length == 0 || spawnIds[0] == 0
            ? spawnPointIdsInBox.Any()
            : spawnPointIdsInBox.Any(id => id > 0 && spawnIds.Contains(id));
        return any != negate;
    }

    public bool NpcIsDeadByStringId(string stringId) {
        DebugLog("[NpcIsDeadByStringId] stringId:{StringId}", stringId);
        return Field.StringIdDeaths.IsDead(stringId, Field.Mobs.Values.Select(mob => (mob.Value.Metadata.Basic.StringId, mob.IsDead)));
    }
    #endregion

    private IEnumerable<FieldNpc> MonstersInBox(params int[] boxIds) {
        if (boxIds.Length == 0 || boxIds[0] == 0) {
            return Field.Mobs.Values;
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        return Field.Mobs.Values.Where(mob => boxes.Any(box => box.Contains(mob.Position)));
    }

    private IEnumerable<FieldNpc> NpcsInBox(params int[] boxIds) {
        if (boxIds.Length == 0 || boxIds[0] == 0) {
            return Field.EnumerateNpcs();
        }

        IEnumerable<TriggerBox> boxes = boxIds
            .Select(boxId => Objects.Boxes.GetValueOrDefault(boxId))
            .Where(box => box != null)!;

        return Field.EnumerateNpcs().Where(mob => boxes.Any(box => box.Contains(mob.Position) || box.Intersects(mob.Shape)));
    }

    // Spawn points in rooms the map seals off. 52000138 (Dark Wind HQ Passage) has side rooms behind static doors
    // (sf_fi_cubric_door_A01_, no trigger id: always closed and part of the navmesh); its script spawns a wave in a
    // room and then opens door actors 4001-4006 for it to come out, but the map has no such actors. The monsters
    // stood in the rooms behind the walls, out of reach, and the escape waits on their deaths. They spawn on the
    // corridor floor nearest their point instead: SealedRoomReach covers the farthest one (about 820 away).
    private static readonly HashSet<(int MapId, int SpawnPointId)> SealedRoomSpawns = [
        .. new[] { 102, 103, 104, 105, 106, 107, 108, 109, 114, 115, 116, 117, 118, 119, 120, 121 }.Select(id => (52000138, id)),
    ];
    public const float SealedRoomReach = 1000;

    public static bool InSealedRoom(int mapId, int spawnPointId) => SealedRoomSpawns.Contains((mapId, spawnPointId));

    // Spawn points the script uses after the player has moved on. 52000138 spawns 112 (act2_wave3_move) back in the
    // first corridor section once the player is in the second one, about 2,200 units behind them; its agents see
    // 2,000 and roam 450 from their point, so it never came and the escape, which waits on 101-123, stalled for a
    // player who did not walk back. It spawns on the line from the nearest player toward its point, at most
    // RearSpawnReach from them, on the floor reachable from the player spawn.
    private static readonly HashSet<(int MapId, int SpawnPointId)> RearSpawns = [(52000138, 112)];
    public const float RearSpawnReach = 1500;
    private const float RearSpawnFloorReach = 300;

    public static bool IsRearSpawn(int mapId, int spawnPointId) => RearSpawns.Contains((mapId, spawnPointId));

    // The spawn point itself when no player is there or it is within reach (on the floor plane); else the point
    // reach away from the player toward it, at the spawn point's height.
    public static Vector3 RearSpawnPoint(Vector3 spawn, Vector3? player, float reach) {
        if (player is not Vector3 at) {
            return spawn;
        }
        var offset = new Vector2(spawn.X - at.X, spawn.Y - at.Y);
        float distance = offset.Length();
        if (distance <= reach) {
            return spawn;
        }
        Vector2 point = new Vector2(at.X, at.Y) + offset / distance * reach;
        return new Vector3(point.X, point.Y, spawn.Z);
    }

    // The event spawn point of that id, else the map's plain npc spawn point of that id: scripts also spawn
    // on plain spawn points, which the event table does not hold.
    public static SpawnPointNPC? FindSpawnPoint(IReadOnlyDictionary<int, EventSpawnPointNPC> eventSpawns, IEnumerable<SpawnPointNPC> npcSpawns, int spawnId) {
        if (eventSpawns.TryGetValue(spawnId, out EventSpawnPointNPC? eventSpawn)) {
            return eventSpawn;
        }
        return npcSpawns.FirstOrDefault(spawn => spawn.SpawnPointId == spawnId);
    }

    private void SpawnNpc(int spawnId, bool useSpawnAnimation = false, int score = 0) {
        SpawnPointNPC? spawn = FindSpawnPoint(Field.Entities.EventNpcSpawns, Field.Entities.NpcSpawns, spawnId);
        if (spawn == null) {
            logger.Warning("[SpawnNpc] Trigger {Trigger} of field {MapId}: no spawn point {SpawnId}", owner.Value.Name, Field.MapId, spawnId);
            return;
        }
        var eventSpawn = spawn as EventSpawnPointNPC;
        // NpcCount caps the list total the way a plain point's own regen does (SpawnPointRules); an event point left
        // at 0 spawns its list (MapEntityMapper.KeepsNpcSpawn).
        int npcCount = SpawnPointRules.PlainCap(spawn);

        DebugLog("[SpawnNpc] spawnId:{SpawnId}, SpawnRadius:{SpawnRadius}, NpcCount:{NpcCount}, Position:{Position}",
            spawnId, spawn.SpawnRadius, npcCount, spawn.Position);

        // NpcCount is the total number of NPCs that can exist at this spawn point.
        // Each NpcList entry's Count is how many of that type can spawn.
        // Build a pool of individual NPC ids from the list, shuffle, and take up to NpcCount.
        List<int> npcPool = [];
        foreach (SpawnPointNPCListEntry entry in spawn.NpcList) {
            for (int j = 0; j < entry.Count; j++) {
                npcPool.Add(entry.NpcId);
            }
        }

        // Shuffle and cap at NpcCount. A group point (Dark Stream) spawns one of its npc groups instead (GroupSpawnRules).
        IEnumerable<int> npcIdsToSpawn = spawn is EventSpawnGroupNPC groupSpawn
            ? GroupSpawnRules.NpcIds(groupSpawn, Random.Shared)
            : npcPool.OrderBy(_ => Random.Shared.Next()).Take(npcCount);

        foreach (int npcId in npcIdsToSpawn) {
            if (!Field.NpcMetadata.TryGet(npcId, out NpcMetadata? npc)) {
                // Not ingested: in KR data every npc file the ingest leaves out has its features off (or only other
                // locales), and spawn points of live maps name some of them (02000499: 21001025, SurvivalContents03).
                logger.Debug("[SpawnNpc] Trigger {Trigger} of field {MapId}: npc {NpcId} of spawn point {SpawnId} is not in the npc table",
                    owner.Value.Name, Field.MapId, npcId, spawnId);
                continue;
            }
            if (npc.Basic.Friendly == 0 && !CanSpawnUnderLimit(Field.TriggerMobLimit, Field.Mobs.Count)) {
                DebugLog("[SpawnNpc] monster limit {Limit} reached, npcId:{NpcId} not spawned", Field.TriggerMobLimit, npcId);
                continue;
            }

            string spawnAnimationString = string.Empty;
            if (!string.IsNullOrEmpty(eventSpawn?.SpawnAnimation) && useSpawnAnimation) {
                spawnAnimationString = eventSpawn.SpawnAnimation;
            }
            Vector3 position = InSealedRoom(Field.MapId, spawnId) ? Field.NearestPlayerFloor(spawn.Position, SealedRoomReach) : spawn.Position;
            if (IsRearSpawn(Field.MapId, spawnId)) {
                Vector3? player = Field.Players.Values.Select(p => (Vector3?) p.Position)
                    .MinBy(at => Vector2.Distance(new Vector2(at!.Value.X, at.Value.Y), new Vector2(spawn.Position.X, spawn.Position.Y)));
                Vector3 rear = RearSpawnPoint(spawn.Position, player, RearSpawnReach);
                if (rear != spawn.Position) {
                    position = Field.NearestPlayerFloor(rear, RearSpawnFloorReach);
                }
            }
            FieldNpc? fieldNpc = Field.SpawnNpc(npc, position, spawn.Rotation, spawnPointNpc: spawn, spawnAnimation: spawnAnimationString);
            if (fieldNpc == null) {
                logger.Error("[SpawnNpc] Failed to spawn npcId:{NpcId}", npcId);
                continue;
            }

            fieldNpc.SpawnPointId = spawnId;
            if (eventSpawn != null) {
                fieldNpc.SpawnPointSequence = NpcControlRules.SpawnSequence(eventSpawn.SpawnAnimation);
            }
            fieldNpc.TriggerScore = score;

            Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
            Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        }
    }
}
