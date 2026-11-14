namespace Maple2.Model.Metadata;

// table/ugccubedefaultcategory.xml (UGCCubeCategoryTable::LoadInteriors, commonfiles/ugccubecategorytable.cpp:285):
// the housing level each cube category (a housing item's categoryIndex) needs, by category. The client reads every
// row and tests no feature or locale; a later row of the same category replaces the earlier one.
public record UgcCubeCategoryTable(IReadOnlyDictionary<int, int> RequireLevels) : Table;
