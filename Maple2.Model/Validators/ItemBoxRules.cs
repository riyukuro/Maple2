namespace Maple2.Model.Validators;

// RequestItemBox: i32 item id, i16 slot, i32 count, the key string.
public static class ItemBoxRules {
    // The client's own value of ItemBoxMultiOpenMaxCount when its table has no row.
    public const int DefaultMultiOpenMax = 20;

    // The client sends the smaller of what is left to open and ItemBoxMultiOpenMaxCount, and only while some is left.
    public static bool IsOpenCount(int count, int multiOpenMax) {
        int max = multiOpenMax > 0 ? multiOpenMax : DefaultMultiOpenMax;
        return count >= 1 && count <= max;
    }
}
