namespace Maple2.Model.Metadata;

// table/characterability.xml, rows already filtered by feature and locale: the abilities of the ability
// window by id, each with the level it needs and the additional effect it gives.
public record CharacterAbilityTable(IReadOnlyDictionary<int, CharacterAbilityTable.Entry> Entries) : Table {
    public record Entry(
        int Id,
        int CategoryId,
        short RequireLevel,
        int AdditionalEffectId,
        short AdditionalEffectLevel);
}
