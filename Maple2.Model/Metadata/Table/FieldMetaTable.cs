namespace Maple2.Model.Metadata;

// table/<locale>/fieldmetadata.xml (CFieldMetaTable::Load, commonfiles/fieldmetatable.cpp).
// Portals: <fieldPortal field=><portal targetField=/></fieldPortal>, the fields each field's portals lead to, of all
// portal types, in file order (GetPortalTargets, the list the client's taxi fare walks). A field listed twice keeps
// its last list, as the client's SetAt does; target 0 is left out.
// NpcFields: <npc id=><field id=/></npc>, the fields an npc stands in (the list the quest window's "go to" button
// checks). The client also drops a field outside its time span in that row's active time, which is not read here.
// IndoorParents: each field's outdoor parent (m_reverse, read by GetParentField): an outdoor field (<fieldPortal
// indoor="0">) whose indoor portals (<portal portalIndoor!=0>), walked on through the indoor portals of the fields they
// reach, lead to it (m_indoorClosure). The client keeps one parent per field: the last outdoor field to reach it in the
// walk order of its fieldPortal map (a CAtlMap, AtlMapOrder), and so does this table.
public record FieldMetaTable(
    IReadOnlyDictionary<int, IReadOnlyList<int>> Portals,
    IReadOnlyDictionary<int, IReadOnlyList<int>> NpcFields,
    IReadOnlyDictionary<int, int>? IndoorParents = null) : Table {
    // The end of CFieldMetaTable::Load: the fieldPortal fields in the map's walk order; from each outdoor one, the fields
    // its indoor portals reach, then theirs, until nothing new (FUN_1406fbdf0); each field reached gets the outdoor field
    // as its parent, a later one replacing an earlier (m_reverse.SetAt). fields: the fieldPortal fields in file order.
    public static Dictionary<int, int> BuildIndoorParents(IReadOnlyList<int> fields, IReadOnlyDictionary<int, bool> outdoor,
                                                          IReadOnlyDictionary<int, IReadOnlyList<int>> indoorPortals) {
        var parents = new Dictionary<int, int>();
        foreach (int field in AtlMapOrder(fields)) {
            if (!outdoor.GetValueOrDefault(field)) continue;
            var seen = new HashSet<int>();
            var reached = new List<int>();
            var next = new List<int> { field };
            while (next.Count > 0) {
                var merged = new List<int>();
                foreach (int from in next) {
                    if (!indoorPortals.TryGetValue(from, out IReadOnlyList<int>? targets)) continue;
                    foreach (int target in targets) {
                        if (seen.Add(target)) {
                            merged.Add(target);
                            reached.Add(target);
                        }
                    }
                }
                next = merged;
            }
            foreach (int child in reached) {
                parents[child] = field;
            }
        }
        return parents;
    }

    // The primes CAtlMap::PickSize picks a bin count from (atlcoll.h).
    private static readonly uint[] AtlPrimes = [
        17, 23, 29, 37, 41, 53, 67, 83, 103, 131, 163, 211, 257, 331, 409, 521, 647, 821, 1031, 1291, 1627, 2053, 2591,
        3251, 4099, 5167, 6521, 8209, 10331, 13007, 16411, 20663, 26017, 32771, 41299, 52021, 65537, 82571, 104033,
    ];

    // The order GetStartPosition/GetNext walk an ATL::CAtlMap<unsigned, ...>(17, 0.75, 0.25, 2.25) holding the keys
    // set in this order: a key hashes to itself and goes to the head of bin key % bins; past 2.25 keys a bin the map
    // rehashes to the first prime at or over count / 0.75, moving the nodes bin by bin, head first, each to the head
    // of its new bin; the walk goes bin by bin, head first. A key set again keeps its place.
    public static List<int> AtlMapOrder(IEnumerable<int> keys) {
        uint bins = 17;
        var table = new List<uint>[bins];
        var placed = new HashSet<uint>();
        foreach (int key in keys) {
            uint hash = (uint) key;
            if (!placed.Add(hash)) continue;
            (table[hash % bins] ??= []).Insert(0, hash);
            if (placed.Count > (uint) (2.25f * bins)) {
                uint want = (uint) (placed.Count / 0.75f);
                uint size = AtlPrimes.FirstOrDefault(prime => prime >= want);
                if (size == 0) size = want;
                if (size != bins) {
                    var rehashed = new List<uint>[size];
                    foreach (List<uint>? bin in table) {
                        if (bin == null) continue;
                        foreach (uint node in bin) {
                            (rehashed[node % size] ??= []).Insert(0, node);
                        }
                    }
                    table = rehashed;
                    bins = size;
                }
            }
        }
        return table.Where(bin => bin != null).SelectMany(bin => bin).Select(hash => (int) hash).ToList();
    }
}
