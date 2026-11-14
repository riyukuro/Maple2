using System.Numerics;

namespace Maple2.Model.Metadata;

public abstract record SpawnPoint(int SpawnPointId, Vector3 Position, Vector3 Rotation, bool Visible) : MapBlock;

public record SpawnPointPC(
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool Enable
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);

public record SpawnPointNPCListEntry(int NpcId, int Count);

public record SpawnPointNPC(
    string EntityId,
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool SpawnOnFieldCreate,
    float SpawnRadius,
    IList<SpawnPointNPCListEntry> NpcList,
    int RegenCheckTime,
    string? PatrolData
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);

public record EventSpawnPointNPC(
    string EntityId,
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool SpawnOnFieldCreate,
    float SpawnRadius,
    int NpcCount,
    IList<SpawnPointNPCListEntry> NpcList,
    int RegenCheckTime,
    int LifeTime,
    string SpawnAnimation,
    // The point's MS2PatrolData uuid (no dashes), as on a plain point: 484 KR event points name one, and an npc a
    // trigger creates there walks it (52020036_qd's 10060 and 10061 get no move order).
    string? PatrolData = null
) : SpawnPointNPC(EntityId, SpawnPointId, Position, Rotation, Visible, SpawnOnFieldCreate, SpawnRadius, NpcList, RegenCheckTime, PatrolData);

// One group of an EventSpawnGroupNPC point: Count npcs, each a random one of NpcIds (the npcs of the server table
// groupSpawn.xml's group GroupId whose tag list holds the point's tag). The point picks one of its groups by Weight.
public record SpawnGroupEntry(int GroupId, int Count, int Weight, int[] NpcIds);

// An event spawn point that names npc groups instead of npcs (EventSpawnGroupNPC: the Dark Stream maps 03100001-015
// and 52010038). NpcCount is the client's field (1); the picked group's Count is what spawns.
public record EventSpawnGroupNPC(
    string EntityId,
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    bool Visible,
    bool SpawnOnFieldCreate,
    float SpawnRadius,
    int NpcCount,
    int RegenCheckTime,
    int LifeTime,
    string SpawnAnimation,
    string GroupTag,
    IList<SpawnGroupEntry> Groups
) : EventSpawnPointNPC(EntityId, SpawnPointId, Position, Rotation, Visible, SpawnOnFieldCreate, SpawnRadius, NpcCount, new List<SpawnPointNPCListEntry>(), RegenCheckTime, LifeTime, SpawnAnimation);

public record EventSpawnPointItem(
    int SpawnPointId,
    Vector3 Position,
    Vector3 Rotation,
    float Lifetime,
    int IndividualDropBoxId,
    int GlobalDropBoxId,
    int GlobalDropLevel,
    bool Visible
) : SpawnPoint(SpawnPointId, Position, Rotation, Visible);
