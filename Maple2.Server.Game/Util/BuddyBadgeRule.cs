using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

// BuddyBadge 3: the partner name kept on a buddy badge follows a rename of the partner.
public static class BuddyBadgeRule {
    // The couple info with the partner's current name, or null when the badge belongs to another
    // partner or already shows that name.
    public static ItemCoupleInfo? Renamed(ItemCoupleInfo info, long partnerId, string currentName) {
        if (partnerId == 0 || info.CharacterId != partnerId || string.IsNullOrEmpty(currentName) || info.Name == currentName) {
            return null;
        }

        return new ItemCoupleInfo(info.CharacterId, currentName, info.IsCreator);
    }
}
