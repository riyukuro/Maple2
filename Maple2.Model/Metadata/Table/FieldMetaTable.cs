namespace Maple2.Model.Metadata;

// table/<locale>/fieldmetadata.xml (CFieldMetaTable::Load, commonfiles/fieldmetatable.cpp).
// Portals: <fieldPortal field=><portal targetField=/></fieldPortal>, the fields each field's portals lead to, of all
// portal types, in file order (GetPortalTargets, the list the client's taxi fare walks). A field listed twice keeps
// its last list, as the client's SetAt does; target 0 is left out.
// NpcFields: <npc id=><field id=/></npc>, the fields an npc stands in (the list the quest window's "go to" button
// checks). The client also drops a field outside its time span in that row's active time, which is not read here.
public record FieldMetaTable(
    IReadOnlyDictionary<int, IReadOnlyList<int>> Portals,
    IReadOnlyDictionary<int, IReadOnlyList<int>> NpcFields) : Table;
