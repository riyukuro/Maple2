using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

// The channel's term tags: the TermTag table's terms, each replaced by the term a GM started or ended here
// ("termtag" command). A term is running from its start until its end, and finished from its end on; terms that
// have not started are not sent (the KMS2 load lists finished and running terms only).
public sealed class TermTagState {
    private readonly object mutex = new();
    private readonly Dictionary<int, TermTagTable.Entry> overrides = [];
    // Per term, whether the online clients hold it as running (the edges sent so far); null until the first Edges
    // call, which takes the terms as they are (the players in the channel got them in their entry load).
    private Dictionary<int, bool>? sent;

    public (List<TermTagTable.Entry> Finished, List<TermTagTable.Entry> Running) Split(TermTagTable table, long now) {
        var finished = new List<TermTagTable.Entry>();
        var running = new List<TermTagTable.Entry>();
        foreach (TermTagTable.Entry entry in Terms(table)) {
            if (entry.Start > now) continue;
            (entry.End <= now ? finished : running).Add(entry);
        }
        return (finished, running);
    }

    // The term of this name (the table row, or this channel's GM change of it). The client's term map is keyed by
    // the exact name (CGameRule::GetTermTag, an unordered_map of CStringW with a byte hash, gamerule.cpp:1323 and
    // gamerule.h:43), so the content gates match case-sensitively; the GM command passes OrdinalIgnoreCase.
    public TermTagTable.Entry? Find(TermTagTable table, string name, StringComparison comparison = StringComparison.Ordinal) {
        return Terms(table).FirstOrDefault(entry => string.Equals(entry.Name, name, comparison));
    }

    public static bool Running(TermTagTable.Entry entry, long now) => entry.Start <= now && now < entry.End;

    // A GM change of a term. The command sends its edge with the change, so Edges sends none for it.
    public void Set(TermTagTable.Entry entry, long now) {
        lock (mutex) {
            overrides[entry.Id] = entry;
            if (sent != null) {
                sent[entry.Id] = Running(entry, now);
            }
        }
    }

    // The terms that started or ended since the last call, each edge once: a table term whose start or end passes
    // while players are online, and the end of a GM-started term. The client registers a term on its start and
    // removes it on its end (TermTagManagerCli::OnStart/OnEnd, unit_140ad5f00_termtagmanagercli.cpp:62, :76).
    public (List<TermTagTable.Entry> Started, List<TermTagTable.Entry> Ended) Edges(TermTagTable table, long now) {
        var started = new List<TermTagTable.Entry>();
        var ended = new List<TermTagTable.Entry>();
        lock (mutex) {
            bool first = sent == null;
            sent ??= [];
            foreach (TermTagTable.Entry entry in Terms(table)) {
                bool running = Running(entry, now);
                bool was = sent.GetValueOrDefault(entry.Id);
                sent[entry.Id] = running;
                if (first || running == was) continue;
                (running ? started : ended).Add(entry);
            }
        }
        return (started, ended);
    }

    private List<TermTagTable.Entry> Terms(TermTagTable table) {
        lock (mutex) {
            return table.Entries.Values.Select(entry => overrides.GetValueOrDefault(entry.Id, entry))
                .OrderBy(entry => entry.Id).ToList();
        }
    }
}
