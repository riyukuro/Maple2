using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The NpcEventDrop game event (GameEventNpcEventDrop, commonfiles/gameevent.cpp:2141): the client keeps each running
// row's context in its game rule (CGameRule::AddNpcEventDrop) for the server and reads none of it. A killed npc gives
// each damage dealer the items of the drop boxes of the rows running at the kill that it matches: each condition the
// row sets (non-zero, non-empty) holds. The npc's level is the one the level bounds compare, its class the npcRank.
// The client's npc data has event tags (npcdata.cpp:1507), but the server does not ingest them, so a row with an
// eventTags condition matches no npc.
public static class NpcEventDropRules {
    public static int[] DropIds(IEnumerable<GameEventMetadata> events, int npcId, short npcLevel, int npcClass, int mapId, DateTime localNow) {
        return events
            .Where(meta => meta.Data is NpcEventDrop drop && Matches(drop, npcId, npcLevel, npcClass, mapId) && new GameEvent(meta).IsActive(localNow))
            .OrderBy(meta => meta.Id)
            .SelectMany(meta => ((NpcEventDrop) meta.Data).DropIds)
            .ToArray();
    }

    public static bool Matches(NpcEventDrop drop, int npcId, short npcLevel, int npcClass, int mapId) {
        return (drop.NpcIds.Length == 0 || drop.NpcIds.Contains(npcId))
               && (drop.FieldId == 0 || drop.FieldId == mapId)
               && (drop.MinLevel <= 0 || npcLevel >= drop.MinLevel)
               && (drop.MaxLevel <= 0 || npcLevel <= drop.MaxLevel)
               && (drop.NpcRank == 0 || drop.NpcRank == npcClass)
               && drop.EventTag == 0;
    }
}
