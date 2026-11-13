namespace Maple2.Model.Validators;

// A black market search option key, as the client builds it: group, percent and stat formatted "%01d%01d%03d" and
// read back as a number (FUN_1401bce20 and FUN_1401bceb0, blackmarketitemoption.cpp:262-277; uiblackmarketpage.cpp:902-908).
// Group 0 is a basic stat (its client stat id), group 1 a special one (its special ability id): a table stat whose
// name is no basic stat name is a special one (CBlackMarketTable, blackmarketitemoption.cpp:1099-1110). Percent is
// 1 for a rate (the stat's percent="true" in table/blackmarkettable.xml). So 0..999 is a flat basic stat,
// 1000..1999 a basic rate, 10000..10999 a flat special stat and 11000..11999 a special rate.
public readonly record struct BlackMarketStatKey(bool Special, bool Percent, int Stat) {
    public int Key => (Special ? 10000 : 0) + (Percent ? 1000 : 0) + Stat;

    // Null for a key the client does not build (a group or percent digit other than 0 and 1, or a negative key).
    public static BlackMarketStatKey? Decode(int key) {
        if (key < 0 || key >= 20000) {
            return null;
        }
        int percent = key / 1000 % 10;
        if (percent > 1) {
            return null;
        }
        return new BlackMarketStatKey(key >= 10000, percent == 1, key % 1000);
    }
}
