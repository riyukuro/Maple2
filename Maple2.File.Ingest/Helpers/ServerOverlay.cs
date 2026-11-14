using System.Xml;
using System.Xml.Serialization;
using Maple2.File.Parser.Xml.Table.Server;

namespace Maple2.File.Ingest.Helpers;

// MS2_SERVER_OVERLAY=<dir>: a directory of server pack files laid out as in Server.m2d (table/Server/KR/...). A file
// the directory holds is read instead of the pack's for the tables that take an overlay: WorldChampionBase.xml, and
// table/Server/NA/GameEvent.xml, whose rows replace the pack's rows of the same gameEventID (rows of a new id are
// added). It is the way to run ended seasons again with new dates, as an operator's own data. Unset, the ingest is
// unchanged.
public sealed class ServerOverlay {
    public const string Option = "MS2_SERVER_OVERLAY";
    public const string GameEventPath = "table/Server/NA/GameEvent.xml";

    private static readonly XmlSerializer GameEventSerializer = new(typeof(GameEventRoot));

    public static readonly ServerOverlay None = new(null);

    public string? Root { get; }

    public ServerOverlay(string? root) {
        Root = string.IsNullOrWhiteSpace(root) ? null : root;
    }

    // The directory must exist when the option is set: a typo would ingest the pack's data without a word.
    public static ServerOverlay FromEnvironment() {
        string? root = Environment.GetEnvironmentVariable(Option);
        if (string.IsNullOrWhiteSpace(root)) {
            return None;
        }
        if (!Directory.Exists(root)) {
            throw new DirectoryNotFoundException($"{Option} is set, but there is no directory at path: {root}");
        }
        return new ServerOverlay(root);
    }

    // The overlay's text of a pack path, or null when the overlay has no such file.
    public string? Read(string packPath) {
        if (Root == null) {
            return null;
        }
        string file = Path.Combine(Root, packPath.Replace('/', Path.DirectorySeparatorChar));
        return System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file) : null;
    }

    // The pack's game events with the overlay's rows put in by id: a row of an id the pack has takes its place, a
    // new id is added after the pack's rows.
    public IEnumerable<(int Id, GameEvent GameEvent)> GameEvents(IEnumerable<(int Id, GameEvent GameEvent)> parsed) {
        string? text = Read(GameEventPath);
        return text == null ? parsed : MergeGameEvents(parsed, ParseGameEvents(text));
    }

    public static IReadOnlyList<GameEvent> ParseGameEvents(string xml) {
        using XmlReader reader = XmlReader.Create(new StringReader(xml));
        var root = (GameEventRoot?) GameEventSerializer.Deserialize(reader);
        return root?.gameEvent.ToList() ?? [];
    }

    public static IEnumerable<(int Id, GameEvent GameEvent)> MergeGameEvents(IEnumerable<(int Id, GameEvent GameEvent)> parsed, IReadOnlyList<GameEvent> overlay) {
        var byId = new Dictionary<int, GameEvent>();
        foreach (GameEvent row in overlay) {
            byId[row.gameEventID] = row;
        }
        var used = new HashSet<int>();
        foreach ((int id, GameEvent row) in parsed) {
            if (byId.TryGetValue(id, out GameEvent? replaced)) {
                used.Add(id);
                yield return (id, replaced);
            } else {
                yield return (id, row);
            }
        }
        foreach (GameEvent row in overlay) {
            if (used.Add(row.gameEventID)) {
                yield return (row.gameEventID, row);
            }
        }
    }
}
