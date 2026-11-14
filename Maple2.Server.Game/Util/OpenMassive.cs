namespace Maple2.Server.Game.Util;

/// <summary>
/// The OpenMassive item function (fieldID, portalDurationTick, maxCount, weddingParty). The client only checks the
/// spot and the portal count around the player (UIMessageBox::HandleOpenMassiveButton) and never maxCount, so the
/// server holds the limit at the portal of the item's own room.
/// </summary>
public static class OpenMassive {
    /// <summary>The named portals (a massive or event portal with its owner's name) a field holds before the client
    /// opens no more (s_msg_item_open_massive_error_too_many_portal).</summary>
    public const int MaxNamedPortals = 10;

    /// <summary>
    /// The client's own refusals before it sends the open (UIMessageBox::HandleOpenMassiveButton, uimessagebox.cpp:2193):
    /// standing in a portal's box (s_msg_item_open_massive_error_another_portal), a field whose field type is not 0
    /// (FUN_140930b10, a dungeon or room; s_msg_item_open_massive_cant_open), a state other than standing
    /// (s_msg_item_open_massive_invalid_state; the server only knows a dead player is not) and 10 portals with an
    /// owner name in the field. It shows its notice and sends nothing, so the server drops such an open and keeps the
    /// item. The content shutdown flag 0x3b is the client's alone, as the server refuses no shut content.
    /// </summary>
    public static bool CanOpen(bool normalField, bool dead, bool inPortalBox, int namedPortals) {
        return normalField && !dead && !inPortalBox && namedPortals < MaxNamedPortals;
    }

    /// <summary>The players the portal takes: the item's maxCount, 0 (no limit) when it is missing or not above 0.</summary>
    public static int MaxCount(IReadOnlyDictionary<string, string> parameters) {
        return parameters.TryGetValue("maxCount", out string? text) && int.TryParse(text, out int count) && count > 0 ? count : 0;
    }
}
