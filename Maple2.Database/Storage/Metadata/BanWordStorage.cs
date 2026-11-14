using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

// The banned words: the static table (ban-word metadata) and the live list GMs keep (banword command,
// DynamicBanWordRules). A live word counts in every check a static one does, whatever its chat type: Block and Mask only
// tell the client what to do with a chat line; a name, title, memo or search text holding the word is refused.
public class BanWordStorage {
    // How long a server may hold the live list before it reads it again. The channels also reload it at once when a GM
    // changes it (ReloadDynamicWords); the login server has no such push and sees a change within this time.
    public static readonly TimeSpan DynamicRefresh = TimeSpan.FromSeconds(10);

    private readonly MetadataContext context;
    private readonly Func<IEnumerable<string>>? loadDynamic;
    private readonly object dynamicLock = new();
    private volatile string[] dynamicWords = [];
    private bool dynamicLoaded;
    private long dynamicLoadedAt;

    private HashSet<string> banWords = new(StringComparer.CurrentCultureIgnoreCase);
    private HashSet<string> ugcBanWords = new(StringComparer.CurrentCultureIgnoreCase);

    // Static words only.
    public BanWordStorage(MetadataContext context) {
        this.context = context;
    }

    public BanWordStorage(MetadataContext context, GameStorage gameStorage) : this(context, () => {
        using GameStorage.Request db = gameStorage.Context();
        return db.GetDynamicBanWords().Select(word => word.Word).ToList();
    }) { }

    private BanWordStorage(MetadataContext context, Func<IEnumerable<string>> loadDynamic) {
        this.context = context;
        this.loadDynamic = loadDynamic;
    }

    // The live words from loadDynamic instead of the game database (tests).
    public static BanWordStorage WithDynamicWords(MetadataContext context, Func<IEnumerable<string>> loadDynamic) {
        return new BanWordStorage(context, loadDynamic);
    }

    // Reads the live list now (a GM changed it).
    public void ReloadDynamicWords() {
        if (loadDynamic == null) {
            return;
        }
        lock (dynamicLock) {
            Load();
        }
    }

    private string[] DynamicWords() {
        if (loadDynamic == null) {
            return [];
        }
        if (!DynamicStale()) {
            return dynamicWords;
        }
        // Only the first read waits for the db. A refresh is done by one caller; the others keep the words held
        // meanwhile instead of waiting for a slow read.
        if (!Volatile.Read(ref dynamicLoaded)) {
            lock (dynamicLock) {
                if (DynamicStale()) {
                    Load();
                }
            }
            return dynamicWords;
        }
        if (Monitor.TryEnter(dynamicLock)) {
            try {
                if (DynamicStale()) {
                    Load();
                }
            } finally {
                Monitor.Exit(dynamicLock);
            }
        }
        return dynamicWords;
    }

    private bool DynamicStale() {
        return !Volatile.Read(ref dynamicLoaded) || Environment.TickCount64 - dynamicLoadedAt >= (long) DynamicRefresh.TotalMilliseconds;
    }

    // Under dynamicLock. A failed read keeps the words held and waits a refresh before the next try.
    private void Load() {
        try {
            dynamicWords = loadDynamic!()
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .ToArray();
        } catch (Exception) {
            // keep the words held
        }
        dynamicLoadedAt = Environment.TickCount64;
        Volatile.Write(ref dynamicLoaded, true);
    }

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
        return banWords.Concat(DynamicWords())
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
        return ugcBanWords.Concat(DynamicWords()).Any(bw => word.Contains(bw, StringComparison.CurrentCultureIgnoreCase));
    }
}
