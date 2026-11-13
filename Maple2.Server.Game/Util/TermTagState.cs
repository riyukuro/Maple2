using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The channel's term tags: the TermTag table's terms, each replaced by the term a GM started or ended here
// ("termtag" command). A term is running from its start until its end, and finished from its end on; terms that
// have not started are not sent (the KMS2 load lists finished and running terms only).
public sealed class TermTagState {
    private readonly object mutex = new();
    private readonly Dictionary<int, TermTagTable.Entry> overrides = [];

    public (List<TermTagTable.Entry> Finished, List<TermTagTable.Entry> Running) Split(TermTagTable table, long now) {
        var finished = new List<TermTagTable.Entry>();
        var running = new List<TermTagTable.Entry>();
        foreach (TermTagTable.Entry entry in Terms(table)) {
            if (entry.Start > now) continue;
            (entry.End <= now ? finished : running).Add(entry);
        }
        return (finished, running);
    }

    // The term of this name (the table row, or this channel's GM change of it).
    public TermTagTable.Entry? Find(TermTagTable table, string name) {
        return Terms(table).FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Running(TermTagTable.Entry entry, long now) => entry.Start <= now && now < entry.End;

    public void Set(TermTagTable.Entry entry) {
        lock (mutex) {
            overrides[entry.Id] = entry;
        }
    }

    private List<TermTagTable.Entry> Terms(TermTagTable table) {
        lock (mutex) {
            return table.Entries.Values.Select(entry => overrides.GetValueOrDefault(entry.Id, entry))
                .OrderBy(entry => entry.Id).ToList();
        }
    }
}
