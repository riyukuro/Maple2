using Maple2.Model.Game;

namespace Maple2.Model.Validators;

// The live ban-word list (DynamicBanword 0x124), set by GMs only (the banword command); the server ships no words.
//
// Client: CDynamicBanWordManager keeps one list with a version. The game scene sends the version it holds on each
// server entry (CSceneStateGame::OnServerEnter, 0 while it holds none). DynamicBanword 0x124 (Read, 0x14034a2c0): a
// byte mode and a long version, then for mode 0 nothing (the list is dropped), mode 1 the whole list (short count,
// BannedWordData each), mode 2 changes to the list held (short count, BannedWordData and a short: 1 add or replace by
// id, 2 remove). UIMessengerBrowser runs each UserChat 0x1D line through it (FilterText): a Mask word is replaced, a
// Block word drops the line.
//
// Chosen here: the version is a hash of the list (Version), so every channel names the same list the same way with
// nothing stored for it; a client whose version differs gets the whole list (mode 1), or mode 0 when the list is
// empty. A GM change sends the whole list to every player online (mode 2 is not used: a client that missed a change
// would apply the next one to the wrong list).
public static class DynamicBanWordRules {
    public const int Block = 0;
    public const int Mask = 1;

    public const byte ModeClear = 0;
    public const byte ModeLoad = 1;

    // Chosen here (the client reads the count as a short).
    public const int MaxWordLength = 50;
    public const int MaxWords = 1000;
    // A 1-letter word would mask or drop nearly every chat line.
    public const int MinWordLength = 2;

    // 0 for the empty list, the client's version while it holds none. The FNV multiplies wrap on purpose (unchecked:
    // the Debug build checks overflow).
    public static long Version(IEnumerable<DynamicBanWord> words) {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        ulong hash = offset;
        bool any = false;
        foreach (DynamicBanWord word in words.OrderBy(word => word.Id)) {
            any = true;
            hash = Add(hash, word.Id);
            hash = Add(hash, word.Type);
            hash = AddText(hash, word.Word);
            hash = AddText(hash, word.Replacement);
        }
        if (!any) {
            return 0;
        }
        long version = unchecked((long) hash);
        return version == 0 ? 1 : version;

        static ulong Add(ulong value, long field) {
            for (int shift = 0; shift < 64; shift += 16) {
                value = unchecked((value ^ (ulong) ((field >> shift) & 0xFFFF)) * prime);
            }
            return value;
        }

        static ulong AddText(ulong value, string field) {
            value = Add(value, field.Length);
            foreach (char c in field) {
                value = unchecked((value ^ c) * prime);
            }
            return value;
        }
    }

    // The mode to send a client holding clientVersion, or null when it holds this list already.
    public static byte? ModeFor(long clientVersion, ICollection<DynamicBanWord> words) {
        long version = Version(words);
        if (clientVersion == version) {
            return null;
        }
        return words.Count == 0 ? ModeClear : ModeLoad;
    }

    // Null when the word can be added, else why not.
    public static string? Refusal(string word, string replacement, ICollection<DynamicBanWord> words) {
        if (string.IsNullOrWhiteSpace(word)) {
            return "The word is empty.";
        }
        if (word.Trim().Length < MinWordLength) {
            return $"The word is at least {MinWordLength} letters.";
        }
        if (word.Length > MaxWordLength || replacement.Length > MaxWordLength) {
            return $"The word and its replacement are at most {MaxWordLength} letters.";
        }
        // The client strips the tags of a chat line before the filter (FUN_140d2c650) and puts the replacement in as
        // it is: a tag in the replacement would go into every matching line, and a word with one never matches.
        if (word.IndexOfAny(['<', '>']) >= 0 || replacement.IndexOfAny(['<', '>']) >= 0) {
            return "The word and its replacement cannot hold < or >.";
        }
        if (words.Count >= MaxWords) {
            return $"The list holds {MaxWords} words at most.";
        }
        string lower = word.ToLowerInvariant();
        DynamicBanWord? same = words.FirstOrDefault(other => other.Word.ToLowerInvariant() == lower);
        return same == null ? null : $"'{same.Word}' is on the list already (#{same.Id}).";
    }
}
