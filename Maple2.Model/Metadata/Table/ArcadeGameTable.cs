namespace Maple2.Model.Metadata;

// table/arcadegame.xml: the arcade lobby's games (the client's CArcadeMainData, arcademaindata.cpp:105). Each row
// is a lobby button: its sorting order, the field the button takes the player to (0 disables the button) and
// whether it is an event arcade (the button's event band; the client lists one only while
// GameEventValueArcadeOpen names it). Rows of a feature that is off are left out, as the client leaves them out.
public record ArcadeGameTable(IReadOnlyDictionary<int, ArcadeGameTable.Entry> Entries) : Table {
    public record Entry(int Id, int SortingOrder, int TargetField, bool IsEventArcade);
}
