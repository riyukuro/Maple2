using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// The rows of the home a player stands in (HomePermissionRules.Blocks): the client checks a row before it sends the
// act it covers, and the server refuses the same act where the row stops the player. The field's home holds the rows
// (RequestCube 42 and 43 keep it current). Outside a home no row applies. A home PvP match's rows stop everyone while
// the fight runs (HomePvpField.Overlays), as the clients have them then.
public static class HomeRowCheck {
    public static bool Blocks(GameSession session, HomePermission row) {
        if (session.Field is HomeFieldManager { PvpZone: HomePvpField match } && match.Overlays(row)) {
            return true;
        }
        return session.Field is HomeFieldManager home && HomePermissionRules.Blocks(home.Home.Permissions, row,
            home.OwnerId, session.AccountId,
            () => session.Party?.Party?.Members.Values.Any(member => member.AccountId == home.OwnerId) == true);
    }
}
