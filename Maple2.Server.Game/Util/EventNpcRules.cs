using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The npcs a SpawnNpc game event puts in a field (the client lists each one in its field for the event's time,
// tableexporter.cpp:906): the rows of this field running at localNow, in id order.
public static class EventNpcRules {
    public static IEnumerable<SpawnNpcEvent> ForField(IEnumerable<GameEventMetadata> events, int mapId, DateTime localNow) {
        return events
            .Where(meta => meta.Data is SpawnNpcEvent spawn && spawn.FieldId == mapId && new GameEvent(meta).IsActive(localNow))
            .OrderBy(meta => meta.Id)
            .Select(meta => (SpawnNpcEvent) meta.Data);
    }

    // The shop the npc opens: the event's shop, else the npc's own (0: none).
    public static int ShopId(int eventShopId, int npcShopId) {
        return eventShopId > 0 ? eventShopId : npcShopId;
    }
}
