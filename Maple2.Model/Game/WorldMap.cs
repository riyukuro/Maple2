using System.Runtime.InteropServices;

namespace Maple2.Model.Game;

// An npc icon of the world map (SpecialNPCInfo::Read): one icon per map, its npc and type from the map's first entry,
// the channels (above 0) of its entries in the tooltip (WorldMapManager::LoadSpecialNpcs).
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 19)]
public struct MapWorldBoss {
    // The tooltip lists the channels, a click opens the boss's reward preview.
    public const byte WorldBossType = 1;
    // The tooltip shows all channels, a click opens the world champion window (UIWorldMapDialog OnBoss).
    public const byte WorldChampionType = 2;

    public int NpcId;
    public int MapId;
    public short Channel;
    public long SpawnTime;
    public byte Type;

    public MapWorldBoss(int npcId, int mapId, short channel, long spawnTime, byte type = WorldBossType) {
        NpcId = npcId;
        MapId = mapId;
        Channel = channel;
        SpawnTime = spawnTime;
        Type = type;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 10)]
public struct MapPopulation {
    public int MapId;
    public int Population; // (1=3 icon, 2=2 icon, 3=1 icon)
    public short Channel;
}
