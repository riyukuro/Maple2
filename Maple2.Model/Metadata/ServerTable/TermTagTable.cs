namespace Maple2.Model.Metadata;

// table/Server/TermTag.xml: the named terms (event periods) a client tests with CGameRule::HasTermTag, e.g. the
// nurturing cubes' NurturingQuestTag. Start and End are Unix seconds. Entries: term id -> term.
public record TermTagTable(IReadOnlyDictionary<int, TermTagTable.Entry> Entries) : ServerTable {
    public record Entry(int Id, string Name, long Start, long End);
}
