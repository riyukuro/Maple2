using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

// The one check for entering a home, for every way in: the visit-home request, the door of an
// outdoor plot and a friend-home portal cube.
public static class HomeEntry {
    // True when the session may enter the home. On false the refusal is already sent; for a home
    // with a password and nothing typed that is the password box, whose answer comes back as a visit-home request.
    public static bool TryEnter(GameSession session, Home home, string typed) {
        bool isOwner = home.AccountId == session.AccountId;
        bool isBlocked = false;
        if (!isOwner) {
            using GameStorage.Request db = session.GameStorage.Context();
            isBlocked = db.IsHomeBlocked(home.AccountId, session.AccountId);
        }

        switch (HomeOpenRules.CheckEnter(isOwner, isBlocked, home.Passcode, typed)) {
            case HomeOpenRules.Enter.Allow:
                return true;
            case HomeOpenRules.Enter.Blocked:
                session.Send(HomeBlockPacket.Result(HomeBlockPacket.Error.EntranceBlocked));
                return false;
            case HomeOpenRules.Enter.AskPassword:
                session.Send(EnterUgcMapPacket.RequestPassword(home.AccountId));
                return false;
            case HomeOpenRules.Enter.WrongPassword:
                session.Send(EnterUgcMapPacket.WrongPassword(home.AccountId));
                return false;
            case HomeOpenRules.Enter.Private:
                // s_home_visit_failed_private (EnterUgcMap 11); it was the homeless line (6).
                session.Send(EnterUgcMapPacket.Private());
                return false;
            default:
                session.Send(EnterUgcMapPacket.CannotVisitThatCharacterHome());
                return false;
        }
    }
}
