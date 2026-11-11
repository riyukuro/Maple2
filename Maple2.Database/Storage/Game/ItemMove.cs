namespace Maple2.Database.Storage;

// The result of GameStorage.Request.PutOnFittingDoll.
public enum ItemMove {
    Moved,
    TargetGone, // the fitting doll's cube is no longer placed
    Failed, // the save failed; nothing changed
}
