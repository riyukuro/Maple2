namespace Maple2.Server.Game.Util;

// RequestCube 70 confirms the bill the server sent for a blueprint item. The client names the item by uid;
// only the item whose use staged the blueprint is accepted, and only while that blueprint is still staged.
public static class BlueprintBillRule {
    public static bool Confirms(bool staged, long stagedItemUid, long requestItemUid) {
        return staged && stagedItemUid != 0 && stagedItemUid == requestItemUid;
    }
}
