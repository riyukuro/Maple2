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
}
