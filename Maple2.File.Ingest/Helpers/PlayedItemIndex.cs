using System.Runtime.CompilerServices;
using System.Xml;
using M2dXmlGenerator;
using Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.File.Ingest.Helpers;

// What the table mappers need of the played client's item data (itemdata/<nnn>.xml), read once per pack: the item
// ids (PlayedClientItems.Ids) and each item's function name (SurvivalPassRewards.PlayedFunctions), both by the
// environments the feature and locale filter keeps. The files are about 100 MB of xml, so the documents are not
// kept, only these. The index keeps the feature and locale filter of its first Of() call for that pack: the ingest
// sets the filter before any mapper runs, and a test that changes it reads a new pack.
public sealed class PlayedItemIndex {
    private static readonly ConditionalWeakTable<M2dReader, PlayedItemIndex> Cache = new();

    public readonly HashSet<int> Ids = [];
    public readonly Dictionary<int, string> Functions = new();

    public static PlayedItemIndex Of(M2dReader playedReader) => Cache.GetValue(playedReader, Read);

    private static PlayedItemIndex Read(M2dReader playedReader) {
        Func<XmlElement, bool> available = node =>
            FeatureLocaleFilter.FeatureEnabled(node.GetAttribute("feature")) && FeatureLocaleFilter.HasLocale(node.GetAttribute("locale"));
        var index = new PlayedItemIndex();
        foreach (var file in playedReader.Files.Where(file => PlayedClientItems.IsItemFile(file.Name))) {
            XmlDocument document = playedReader.GetXmlDocument(file);
            index.Ids.UnionWith(PlayedClientItems.Ids([document], available));
            foreach ((int id, string name) in SurvivalPassRewards.PlayedFunctions([document], available)) {
                index.Functions[id] = name;
            }
        }
        Console.WriteLine($"played client: {index.Ids.Count} items in its item data");
        return index;
    }
}
