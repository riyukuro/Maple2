namespace Maple2.Database.Storage;

// The result of GameStorage.Request.PutOnFittingDoll and SwapFittingDoll.
public enum ItemMove {
    Moved,
    TargetGone, // the fitting doll's cube is no longer placed
    SourceGone, // an item to take off the doll is no longer on it
    Failed, // the save failed; nothing changed
}
