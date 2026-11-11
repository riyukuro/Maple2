namespace Maple2.Model.Validators;

// RequestTaxi 0x0048. Modes 0 and 5 have no body: the client sends 0 from the taxi station's action
// key and 5 from its station finder when the station's field is not in its list of known stations.
// Modes 1, 3 and 4 name the destination field.
public static class TaxiRules {
    public const byte DiscoverAtStation = 0;
    public const byte DiscoverByFinder = 5;

    public static bool IsDiscover(byte mode) {
        return mode is DiscoverAtStation or DiscoverByFinder;
    }

    // A station is found once, and only in a field that has one.
    public static bool CanDiscover(bool known, bool fieldHasStation) {
        return !known && fieldHasStation;
    }
}
