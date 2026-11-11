using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.Tools;

namespace Maple2.File.Ingest;

// TableParser reads some tables from table/<locale>/ in Xml.m2d, and a client pack carries only its own
// region's folder (the NA pack table/na, the KMS pack table/kr). Those files follow the pack, while the
// feature/locale row filter follows LOCALE, the client's region. TableParser takes the folder name from
// FeatureLocaleFilter.Locale when it is built, so it is built with the pack's locale set for that moment.
public static class PackTables {
    public static string Locale { get; private set; } = string.Empty;

    // The pack's region folder: LOCALE's own when the pack has it, else the one region folder it has.
    public static void Load(M2dReader xmlReader, string rowLocale) {
        string[] folders = RegionFolders(xmlReader.Files.Select(file => file.Name));
        if (folders.Contains(rowLocale)) {
            Locale = rowLocale;
        } else if (folders.Length == 1) {
            Locale = folders[0];
        } else {
            throw new InvalidOperationException($"Xml.m2d has no table/{rowLocale.ToLower()} folder and {folders.Length} other region folders ({string.Join(", ", folders)})");
        }
    }

    // The region folders (table/<locale>/, upper case) among a pack's file names.
    public static string[] RegionFolders(IEnumerable<string> fileNames) {
        return fileNames
            .Select(name => name.Split('/'))
            .Where(parts => parts.Length >= 3 && parts[0] == "table")
            .Select(parts => parts[1].ToUpperInvariant())
            .Where(folder => Array.IndexOf(Maple2.Tools.Locale.Allowed, folder) >= 0)
            .Distinct()
            .ToArray();
    }

    // The played client's pack (MS2_PLAYED_CLIENT_XML) carries its own region's folder only. When that region is
    // not LOCALE, the server runs another region's script branches and rows than the client it serves (e.g. a KR
    // client stages an outfit for dismantling by its KR script branch, while LOCALE=NA previews and pays nothing).
    // Null when they match or the pack's region cannot be told (no region folder, or more than one).
    public static string? PlayedLocaleWarning(string[] playedFolders, string locale) {
        if (playedFolders.Length != 1 || string.Equals(playedFolders[0], locale, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        return $"WARNING: LOCALE is {locale}, but the played client's pack (MS2_PLAYED_CLIENT_XML) is {playedFolders[0]}. " +
               $"Run the ingest and the servers with LOCALE={playedFolders[0]}, or the server answers by another region's rules than the client's.";
    }

    public static TableParser NewParser(M2dReader xmlReader, string language) {
        string rowLocale = FeatureLocaleFilter.Locale;
        FeatureLocaleFilter.Locale = Locale;
        try {
            return new TableParser(xmlReader, language);
        } finally {
            FeatureLocaleFilter.Locale = rowLocale;
        }
    }
}
