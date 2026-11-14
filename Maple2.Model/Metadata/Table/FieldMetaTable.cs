namespace Maple2.Model.Metadata;

// table/<locale>/fieldmetadata.xml (CFieldMetaTable::Load, commonfiles/fieldmetatable.cpp).
// Portals: <fieldPortal field=><portal targetField=/></fieldPortal>, the fields each field's portals lead to, of all
// portal types, in file order (GetPortalTargets, the list the client's taxi fare walks). A field listed twice keeps
// its last list, as the client's SetAt does; target 0 is left out.
// NpcFields: <npc id=><field id=/></npc>, the fields an npc stands in (the list the quest window's "go to" button
// checks). The client also drops a field outside its time span in that row's active time, which is not read here.
// IndoorParents: the outdoor fields (<fieldPortal indoor="0">) whose indoor portals (<portal portalIndoor!=0>), walked
// on through the indoor portals of the fields they reach, lead to each field (m_indoorClosure, read back through
// m_reverse by GetParentField). The client keeps one parent per field, the last outdoor field its hash walk reaches;
// all of them are kept here, so a check against them is never stricter than the client's.
public record FieldMetaTable(
    IReadOnlyDictionary<int, IReadOnlyList<int>> Portals,
    IReadOnlyDictionary<int, IReadOnlyList<int>> NpcFields,
    IReadOnlyDictionary<int, IReadOnlyList<int>>? IndoorParents = null) : Table {
    // The end of CFieldMetaTable::Load: from each outdoor field, the fields its indoor portals reach, then theirs, until
    // nothing new (FUN_1406fbdf0); each field reached gets the outdoor field as a parent.
    public static Dictionary<int, IReadOnlyList<int>> BuildIndoorParents(IReadOnlyDictionary<int, bool> outdoor, IReadOnlyDictionary<int, IReadOnlyList<int>> indoorPortals) {
        var parents = new Dictionary<int, List<int>>();
        foreach ((int field, bool isOutdoor) in outdoor) {
            if (!isOutdoor) continue;
            var reached = new List<int>();
            var seen = new HashSet<int>();
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
                if (!parents.TryGetValue(child, out List<int>? list)) {
                    parents[child] = list = [];
                }
                list.Add(field);
            }
        }
        return parents.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<int>) entry.Value);
    }
}
