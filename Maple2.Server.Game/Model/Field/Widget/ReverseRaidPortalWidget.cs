using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Model.Widget;

/// <summary>The reverse raid hall's portals (see <see cref="ReverseRaidPortalRules"/>). The field cannot read the
/// channel's events, so the portals are set when a player enters the hall: before the field's portals are sent to
/// that player, and the players already there get each change.</summary>
public class ReverseRaidPortalWidget : Widget {
    private readonly HashSet<int> shown = [];

    public ReverseRaidPortalWidget(FieldManager field) : base(field) { }

    // events: the running events' data (the entering player's GameSession.FindEvent); entering: the player who is
    // sent the field's portals next, so the change is not sent twice.
    public void Refresh(IEnumerable<GameEventData?> events, GameSession? entering) {
        HashSet<int> open = ReverseRaidPortalRules.OpenPortals(events,
            roomId => Field.TableMetadata.DungeonRoomTable.Entries.TryGetValue(roomId, out DungeonRoomMetadata? room) ? room.GroupType : null);
        foreach (int portalId in open.Union(shown).ToArray()) {
            bool show = open.Contains(portalId);
            if (show == shown.Contains(portalId) || !Field.TryGetPortal(portalId, out FieldPortal? portal)) {
                continue;
            }
            portal.Visible = show;
            portal.Enabled = show;
            if (show) {
                shown.Add(portalId);
            } else {
                shown.Remove(portalId);
            }
            Field.Broadcast(PortalPacket.Update(portal), entering);
        }
    }
}
