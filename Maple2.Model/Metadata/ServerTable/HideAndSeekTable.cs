namespace Maple2.Model.Metadata;

// table/Server/hideAndSeek.xml: <field mapCode ridingObject="id,id,.." [feature] [locale]/>, the rides a hider of a
// Hide and Seek field turns into (riding type object). A row of this locale replaces the row without a locale. Props:
// for each ride, the ObjectSearchIndex of the map's props (its xblock) whose model is the ride's nif; these are the
// ids HideAndSeek mode 1 hides (WorldService::HideEntities). Entries: map id -> field.
public record HideAndSeekTable(IReadOnlyDictionary<int, HideAndSeekTable.Field> Entries) : ServerTable {
    public record Field(IReadOnlyList<int> Rides, IReadOnlyDictionary<int, IReadOnlyList<int>> Props);
}
