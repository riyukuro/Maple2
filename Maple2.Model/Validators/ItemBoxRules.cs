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

    // An item function's comma parameters as the client reads them (FUN_14025a680, CStringW::Tokenize on ","): empty
    // parts are skipped, so "1,,330001622" is two values, and each part is read by _wtol (white space, a sign, the
    // digits in front; 0 with no number in front).
    public static int[] FunctionArgs(string? parameters) {
        string[] parts = (parameters ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        var args = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) {
            args[i] = Wtol(parts[i]);
        }
        return args;
    }

    // FUN_1408d1fd0: the value at the index, or the fallback past the end.
    public static int Arg(int[] args, int index, int fallback = 0) {
        return index >= 0 && index < args.Length ? args[index] : fallback;
    }

    // _wtol: leading white space, an optional sign, then digits; a value past the int range stops at its limit.
    private static int Wtol(string text) {
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) {
            i++;
        }
        bool negative = false;
        if (i < text.Length && text[i] is '+' or '-') {
            negative = text[i] == '-';
            i++;
        }
        long value = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) {
            value = Math.Min(value * 10 + (text[i] - '0'), (long) int.MaxValue + 1);
            i++;
        }
        value = negative ? -value : value;
        return (int) Math.Clamp(value, int.MinValue, int.MaxValue);
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
