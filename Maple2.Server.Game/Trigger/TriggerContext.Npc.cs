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
        WarnLog("[CreateMonster] spawnIds:{SpawnIds}, autoTarget:{AutoTarget}, delay:{Delay}", string.Join(", ", spawnIds), autoTarget, delay);
        foreach (int spawnId in spawnIds) {
            if (delay > 0) {
                Events.Schedule(() => SpawnNpc(spawnId, autoTarget), TimeSpan.FromMilliseconds(delay));
            } else {
                SpawnNpc(spawnId, autoTarget);
            }
        }
    }

    public void DestroyMonster(int[] spawnIds, bool arg2) {
        WarnLog("[DestroyMonster] spawnIds:{SpawnIds}, Arg2:{Arg2}", string.Join(", ", spawnIds), arg2);
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
            fieldNpc.SetPatrolData(patrolData);
        }
    }

    public void MoveNpcToPos(int spawnId, Vector3 position, Vector3 rotation) {
        WarnLog("[MoveNpcToPos] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Position = position;
                npc.Rotation = rotation;
            }
        }
    }

    public void NpcRemoveAdditionalEffect(int spawnId, int additionalEffectId) {
        WarnLog("[NpcRemoveAdditionalEffect] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.EnumerateNpcs()) {
            if (npc.SpawnPointId == spawnId) {
                npc.Buffs.Remove(additionalEffectId, npc.ObjectId);
            }
        }
    }

    public void NpcToPatrolInBox(int boxId, int npcId, string spawnId, string patrolName) {
        ErrorLog("[NpcToPatrolInBox] boxId:{BoxId}, npcId:{NpcId}, spawnId:{SpawnId}, patrolName:{PatrolName}", spawnId, npcId, spawnId, patrolName);
    }

    public void SetAiExtraData(string key, int value, bool isModify, int boxId) {
        WarnLog("[SetAiExtraData] key:{Key}, value:{Value}, isModify:{IsModify}, boxId:{BoxId}", key, value, isModify, boxId);
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

        if (spawnId == 0) {
            var player = Field.Players.Values.FirstOrDefault();
            if (player == null) {
                return;
            }
            Broadcast(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, player.ObjectId, script, delay * 1000, 0));
            return;
        }

        if (type == 1) {
            var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
            if (npc == null) {
                return;
            }

            Broadcast(CinematicPacket.BalloonTalk(CinematicPacket.BalloonText.Trigger, npc.ObjectId, script, delay * 1000, 0));
            return;
        }

        Broadcast(CinematicPacket.Talk(spawnId, spawnId.ToString(), script, delay * 1000, align));
    }

    public void SetNpcDuelHpBar(bool isOpen, int spawnId, int durationTick, int npcHpStep) {
        DebugLog("[SetNpcDuelHpBar] isOpen:{IsOpen}, spawnId:{SpawnId}, durationTick:{Duration}, npcHpStep:{HpStep}", isOpen, spawnId, durationTick, npcHpStep);
        IActor? actor = Field.GetActorsBySpawnId(spawnId).FirstOrDefault();
        if (actor is null) {
            return;
        }

        Broadcast(TriggerPacket.DuelHpBar(isOpen, actor.ObjectId, durationTick, npcHpStep));
    }

    public void SetNpcEmotionLoop(int spawnId, string sequenceName, float duration) {
        WarnLog("[SetNpcEmotionLoop] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, duration);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.Animate(sequenceName, duration);
    }

    public void SetNpcEmotionSequence(int spawnId, string sequenceName, int durationTick) {
        WarnLog("[SetNpcEmotionSequence] spawnId:{SpawnId}, sequenceName:{SequenceName}, durationTick:{Duration}", spawnId, sequenceName, durationTick);

        FieldNpc? fieldNpc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        fieldNpc?.AnimateSequence(sequenceName);
    }

    public void SetNpcRotation(int spawnId, float rotation) {
        WarnLog("[SetNpcRotation] spawnId:{SpawnId}, rotation:{Rotation}", spawnId, rotation);
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
            SpawnNpc(spawnId, score: score);
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
            logger.Error("[SpawnNpc] Invalid spawnId:{SpawnId}", spawnId);
            return;
        }
        var eventSpawn = spawn as EventSpawnPointNPC;
        // A plain spawn point has no npc count: it spawns its whole list.
        int npcCount = eventSpawn?.NpcCount ?? spawn.NpcList.Sum(entry => entry.Count);

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

        // Shuffle and cap at NpcCount
        IEnumerable<int> npcIdsToSpawn = npcPool.OrderBy(_ => Random.Shared.Next()).Take(npcCount);

        foreach (int npcId in npcIdsToSpawn) {
            if (!Field.NpcMetadata.TryGet(npcId, out NpcMetadata? npc)) {
                logger.Error("[SpawnNpc] Invalid npcId:{NpcId}", npcId);
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
            FieldNpc? fieldNpc = Field.SpawnNpc(npc, spawn.Position, spawn.Rotation, spawnPointNpc: spawn, spawnAnimation: spawnAnimationString);
            if (fieldNpc == null) {
                logger.Error("[SpawnNpc] Failed to spawn npcId:{NpcId}", npcId);
                continue;
            }

            fieldNpc.SpawnPointId = spawnId;
            fieldNpc.TriggerScore = score;

            Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
            Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        }
    }
}
