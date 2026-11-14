using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

// The raid portals of the reverse raid hall (63000054, the only KR script that creates the ReverseRaidPortal widget).
// Its trigger hides portals 20007001 (Zakum) and 20023001 (Pink Bean) and leaves the minimap icon on, with the note
// that code turns the icon on and off with the portal's visibility; then it creates the widget. The client has no
// such widget (the portals are type 8, whose use opens the dungeon room dialog of the portal's id,
// PortalAction::UsePortal, actionkeyobject.cpp:1048). The server pack names the portals the hall opens: the
// ReverseRaidDungeonOpen events (KR 360001-360007, value1 "20007001,20023001"). Chosen here: a portal is shown and
// usable while a running event names it and it is a reverse raid room the server has (KR has no room 20023001).
public static class ReverseRaidPortalRules {
    public static HashSet<int> OpenPortals(IEnumerable<GameEventData?> events, Func<int, DungeonGroupType?> roomGroupType) {
        var open = new HashSet<int>();
        foreach (GameEventData? data in events) {
            if (data is not ReverseRaidDungeonOpen reverseRaid) {
                continue;
            }
            foreach (int portalId in reverseRaid.PortalIds) {
                if (roomGroupType(portalId) == DungeonGroupType.reverseRaid) {
                    open.Add(portalId);
                }
            }
        }
        return open;
    }
}
