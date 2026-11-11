namespace Maple2.Model.Validators;

public static class ItemSocketRules {
    // The client counts no sockets on an item with a skin type (an outfit) while the SkinSocket feature is off
    // (CItemSocketTable::IsSocketFeatureEnabled, commonfiles/itemsocketsystemutil.cpp), so the ingest keeps such an
    // item's socketDataId only when the feature is on for the locale.
    public static int SocketId(int socketDataId, int skinType, bool skinSocketEnabled) {
        return skinType != 0 && !skinSocketEnabled ? 0 : socketDataId;
    }

    // The drop table has no skin type, so a drop row's socketDataID on an outfit is kept only when the outfit still has
    // its own socket id after the ingest's SkinSocket gate (the feature is on); other items always take it.
    public static bool KeepsDropSocketId(int skinType, int itemSocketId) {
        return skinType == 0 || itemSocketId != 0;
    }
}
