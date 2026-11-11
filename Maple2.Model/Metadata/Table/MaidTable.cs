namespace Maple2.Model.Metadata;

// table/maidproperty.xml by MaidID: the maid npc a maid furnishing places and its recipe group.
public record MaidTable(IReadOnlyDictionary<int, MaidTable.Entry> Entries) : Table {
    public record Entry(int MaidId, int RecipeGroupId, int NpcId);

    // A <Property MaidID RecipeGroupID NPCID/> row; null when the maid or npc id is not a positive number.
    public static Entry? Row(string maidId, string recipeGroupId, string npcId) {
        if (!int.TryParse(maidId, out int maid) || maid <= 0 || !int.TryParse(npcId, out int npc) || npc <= 0) {
            return null;
        }
        int.TryParse(recipeGroupId, out int recipeGroup);
        return new Entry(maid, recipeGroup, npc);
    }
}
