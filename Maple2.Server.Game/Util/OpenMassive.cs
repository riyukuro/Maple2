namespace Maple2.Server.Game.Util;

/// <summary>
/// The OpenMassive item function (fieldID, portalDurationTick, maxCount, weddingParty). The client only checks the
/// spot and the portal count around the player (UIMessageBox::HandleOpenMassiveButton) and never maxCount, so the
/// server holds the limit at the portal of the item's own room.
/// </summary>
public static class OpenMassive {
    /// <summary>The players the portal takes: the item's maxCount, 0 (no limit) when it is missing or not above 0.</summary>
    public static int MaxCount(IReadOnlyDictionary<string, string> parameters) {
        return parameters.TryGetValue("maxCount", out string? text) && int.TryParse(text, out int count) && count > 0 ? count : 0;
    }
}
