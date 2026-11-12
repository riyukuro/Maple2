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

    // A select box's pick (UISelectItemBoxDialog): the rows the player picked in the list, comma separated, exactly
    // as many as the box's select count (its first parameter). Null when the text is not that.
    public static int[]? ParseSelection(string text, int selectCount) {
        string[] parts = text.Split(',');
        var rows = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rows[i])) {
                return null;
            }
        }
        if (selectCount < 1 || rows.Length != selectCount || rows.Distinct().Count() != rows.Length) {
            return null;
        }

        return rows;
    }

    public enum LuluPay { None, Box, Meso }

    // What one open of a key box (OpenItemBoxLullu) takes besides the key: a box item when one is held; without
    // one, the box price in meso when the player ticked the pay box and has that much. None: no key, or neither,
    // and then nothing is taken at all.
    public static LuluPay LuluPayment(int keys, int boxes, bool autoPay, long meso, long boxPrice) {
        if (keys < 1) {
            return LuluPay.None;
        }
        if (boxes >= 1) {
            return LuluPay.Box;
        }
        return autoPay && boxPrice >= 0 && meso >= boxPrice ? LuluPay.Meso : LuluPay.None;
    }
}
