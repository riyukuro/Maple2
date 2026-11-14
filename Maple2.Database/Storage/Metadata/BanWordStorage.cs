using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public class BanWordStorage(MetadataContext context) {
    private HashSet<string> banWords = new(StringComparer.CurrentCultureIgnoreCase);
    private HashSet<string> ugcBanWords = new(StringComparer.CurrentCultureIgnoreCase);

    public bool ContainsBannedWord(string word) => FindBannedWord(word) != null;

    // The banned word the text holds, the first in the text (the longest of those at one place), or null. The
    // character create answer names it (s_char_err_ban_any: "Contains a forbidden word ({0}).").
    public string? FindBannedWord(string word) {
        if (banWords.Count == 0) {
            lock (context) {
                if (banWords.Count == 0) {
                    banWords = context.BanWordMetadata
                        .AsNoTracking()
                        .Where(bw => !bw.Ugc)
                        .Select(bw => bw.Value)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                }
            }
        }
        return banWords
            .Select(bw => (Word: bw, Index: word.IndexOf(bw, StringComparison.CurrentCultureIgnoreCase)))
            .Where(found => found.Index >= 0)
            .OrderBy(found => found.Index)
            .ThenByDescending(found => found.Word.Length)
            .Select(found => found.Word)
            .FirstOrDefault();
    }

    public bool ContainsUgcBannedWord(string word) {
        if (ugcBanWords.Count == 0) {
            lock (context) {
                if (ugcBanWords.Count == 0) {
                    ugcBanWords = context.BanWordMetadata
                        .AsNoTracking()
                        .Where(bw => bw.Ugc)
                        .Select(bw => bw.Value)
                        .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                }
            }
        }
        return ugcBanWords.Any(bw => word.Contains(bw, StringComparison.CurrentCultureIgnoreCase));
    }
}
