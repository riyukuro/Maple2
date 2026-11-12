using System.Collections.Generic;

namespace Maple2.Server.Game.Util;

// The treasure map item function (TreasureMap): the parameter names the reward boxes, <v groupId dropId freeDropId/>.
// dropId is rolled when the map is appraised for the cost, freeDropId when the npc opened the dialog with "free".
public static class TreasureMapRules {
    // The cost the dialog shows for a paid appraise. No client or data source gives it (the server sends it) and KMS2
    // has no appraise, so 0 is chosen here.
    public const long AppraiseCost = 0;

    // The answer the client reads as "this map cannot be appraised".
    public const long CannotAppraise = -1;

    public const string FreeArgument = "free";

    public static bool TryParse(string? parameters, out int dropId, out int freeDropId) {
        Dictionary<string, string> values = XmlParseUtil.GetParameters(parameters);
        dropId = values.TryGetValue("dropId", out string? paid) && int.TryParse(paid, out int paidId) ? paidId : 0;
        freeDropId = values.TryGetValue("freeDropId", out string? free) && int.TryParse(free, out int freeId) ? freeId : 0;
        return dropId > 0 || freeDropId > 0;
    }

    // The box rolled and the cost taken, or false when the map has no box for this kind of appraise.
    public static bool TryPick(string? parameters, bool free, out int boxId, out long cost) {
        cost = free ? 0 : AppraiseCost;
        boxId = 0;
        if (!TryParse(parameters, out int dropId, out int freeDropId)) {
            return false;
        }
        boxId = free ? freeDropId : dropId;
        return boxId > 0;
    }
}
