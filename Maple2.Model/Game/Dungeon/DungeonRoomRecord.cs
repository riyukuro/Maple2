using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

public class DungeonRoomRecord {
    public ConcurrentDictionary<long, DungeonUserRecord> UserResults = [];
    public readonly DungeonRoomMetadata Metadata;
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public DungeonState State { get; set; } = DungeonState.None;
    // The trigger dungeon variables of this instance (set_dungeon_variable, dungeon_variable). One record is shared
    // by the lobby and every room field of the instance; an id never set reads 0.
    public readonly ConcurrentDictionary<int, int> Variables = new();

    // The first player who entered the room (dungeon_first_user_mission_score); 0 before anyone did.
    private long firstCharacterId;
    public long FirstCharacterId => Interlocked.Read(ref firstCharacterId);

    public DungeonRoomRecord(DungeonRoomMetadata metadata) {
        Metadata = metadata;
    }

    public void SetFirstUser(long characterId) => Interlocked.CompareExchange(ref firstCharacterId, characterId, 0);

    // The room's clear is kept for its boss board once (a second clear of the instance keeps nothing).
    private int bossClearKept;
    public bool TryKeepBossClear() => Interlocked.Exchange(ref bossClearKept, 1) == 0;

    // The damage the room's players dealt to its bosses (npcs of class 3 and up) before the room had its result, per
    // character: the damage of the world boss board (0xb).
    public readonly ConcurrentDictionary<long, long> BossDamage = new();

    public void AddBossDamage(long characterId, long amount) {
        if (amount <= 0 || State != DungeonState.None) {
            return;
        }
        BossDamage.AddOrUpdate(characterId, amount, (_, total) => total + amount);
    }

    public long TotalBossDamage => BossDamage.Values.Sum();

    // dungeon_disable_ranking: the room's run is left off the Colosseum board (the Colosseum script runs it for a
    // continued game).
    private int rankingDisabled;
    public bool RankingDisabled => Volatile.Read(ref rankingDisabled) == 1;
    public void DisableRanking() => Interlocked.Exchange(ref rankingDisabled, 1);

    /// <summary>The sum of the first user's user mission scores (the client sums the scores the same way).</summary>
    public int FirstUserMissionScore() {
        if (!UserResults.TryGetValue(FirstCharacterId, out DungeonUserRecord? record)) {
            return 0;
        }
        return record.Missions.Values.Where(mission => mission.IsUserMission).Sum(mission => (int) mission.Score);
    }
}
