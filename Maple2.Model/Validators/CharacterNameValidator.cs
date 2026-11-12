using System.Text.RegularExpressions;
using Maple2.Model.Metadata;

namespace Maple2.Model.Validators;

public static partial class NameValidator {
    // Regex patterns for valid names
    private static readonly Regex ValidNamePatternAscii = NamePatternAsciiRegex();
    private static readonly Regex ValidNamePatternUnicode = NamePatternUnicodeRegex();

    /// <summary>
    /// Validates a name according to all rules.
    /// </summary>
    /// <param name="name">The name to validate</param>
    /// <returns>True if valid, false if invalid</returns>
    public static bool ValidName(string name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return false; // Null or whitespace
        }
        // Select pattern
        Regex pattern = Constant.AllowUnicodeInNames ? ValidNamePatternUnicode : ValidNamePatternAscii;
        if (!pattern.IsMatch(name)) {
            return false;
        }

        // Check for names that are only special characters
        if (name.All(c => !char.IsLetterOrDigit(c))) {
            return false;
        }

        return true; // Valid name
    }

    /// <summary>
    /// The client's character-name rule for a region (CBanTextChecker::FUN_140225210 with the short limit,
    /// which runs ScanLimit, commonstringtable.cpp): 2 to 12 characters with no space or punctuation,
    /// ASCII letters and digits, plus by region: KR (and any region the client has no case for) Hangul
    /// syllables U+AC00..U+D7A3, counted twice; NA the umlauts and the Portuguese letters; CN the CJK
    /// ideographs U+4E00..U+9FAE, counted twice. JP adds Hangul and a character class table this does not
    /// model, so JP names beyond ASCII and Hangul are refused.
    /// </summary>
    /// <returns>The counted length, or -1 when a character is not allowed in the region.</returns>
    public static int CharacterNameWidth(string name, string locale) {
        int width = 0;
        foreach (char ch in name) {
            width++;
            if (ch is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z') {
                continue;
            }
            bool hangul = ch is >= '\uAC00' and < '\uD7A4';
            switch (locale) {
                case "NA":
                    if ("\u00C4\u00E4\u00D6\u00F6\u00DC\u00FC\u00DF".IndexOf(ch) >= 0
                        || "\u00AA\u00BA\u00C0\u00C1\u00C2\u00C3\u00C7\u00C9\u00CA\u00CD\u00D3\u00D4\u00D5\u00DA\u00E0\u00E1\u00E2\u00E3\u00E7\u00E9\u00EA\u00ED\u00F3\u00F4\u00F5\u00FA".IndexOf(ch) >= 0) {
                        continue;
                    }
                    return -1;
                case "CN":
                    if (ch is >= '\u4E00' and < '\u9FAF') {
                        width++;
                        continue;
                    }
                    return -1;
                default:
                    if (hangul) {
                        width++;
                        continue;
                    }
                    return -1;
            }
        }
        return width;
    }

    // The count ScanLimit makes before it looks at what the characters are: one per character and one more for a
    // character the region counts twice. A character the region does not allow counts once.
    public static int CharacterNameScanWidth(string name, string locale) {
        int width = name.Length;
        foreach (char ch in name) {
            bool wide = locale switch {
                "NA" => false,
                "CN" => ch is >= '\u4E00' and < '\u9FAF',
                _ => ch is >= '\uAC00' and < '\uD7A4',
            };
            if (wide) {
                width++;
            }
        }
        return width;
    }

    public static bool ValidCharacterName(string name, string locale) {
        int width = CharacterNameWidth(name, locale);
        return width >= Constant.CharacterNameLengthMin && width <= Constant.CharacterNameLengthMax;
    }

    // ASCII only: A-Z, a-z, 0-9 (no dash, no underscore)
    [GeneratedRegex(@"^[A-Za-z0-9]+$", RegexOptions.Compiled)]
    private static partial Regex NamePatternAsciiRegex();

    // Unicode: \p{L} (all letters, including accents), 0-9 (no dash, no underscore)
    [GeneratedRegex(@"^[\p{L}0-9]+$", RegexOptions.Compiled)]
    private static partial Regex NamePatternUnicodeRegex();
}
