namespace Maple2.Model.Metadata;

// table/maidproperty.xml by MaidID: the maid npc a maid furnishing places and its recipe group, with the salary of
// table/maidsalary.xml (CPriceTable::GetSalary). Exps is table/maidexp.xml: the affinity exp of each level
// (CMaidExpTable), whose row count is the top affinity level. Both are empty in data ingested before they were read.
public record MaidTable(IReadOnlyDictionary<int, MaidTable.Entry> Entries, IReadOnlyDictionary<int, int>? Exps = null) : Table {
    // SalaryType 0 is paid in mesos, others in merets (MaidInfo, the maid item tooltip).
    public record Entry(int MaidId, int RecipeGroupId, int NpcId, int SalaryType = 0, int Salary = 0);

    // A <Property MaidID RecipeGroupID NPCID/> row; null when the maid or npc id is not a positive number.
    public static Entry? Row(string maidId, string recipeGroupId, string npcId) {
        if (!int.TryParse(maidId, out int maid) || maid <= 0 || !int.TryParse(npcId, out int npc) || npc <= 0) {
            return null;
        }
        int.TryParse(recipeGroupId, out int recipeGroup);
        return new Entry(maid, recipeGroup, npc);
    }
}
