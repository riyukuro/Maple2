using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// table/maidproperty.xml by MaidID: the maid npc a maid furnishing places and its recipe group, with the salary of
// table/maidsalary.xml (CPriceTable::GetSalary). Exps is table/maidexp.xml: the affinity exp of each level
// (CMaidExpTable), whose row count is the top affinity level. Recipes is table/maidrecipe.xml by recipe id
// (CMaidRecipeTable) and RecipeGroups table/maidrecipegroup.xml by group id (CMaidRecipeGroupTable), the recipes a
// maid crafts. All are empty (or null) in data ingested before they were read.
public record MaidTable(
    IReadOnlyDictionary<int, MaidTable.Entry> Entries,
    IReadOnlyDictionary<int, int>? Exps = null,
    IReadOnlyDictionary<int, MaidTable.Recipe>? Recipes = null,
    IReadOnlyDictionary<int, IReadOnlyList<MaidTable.RecipeLevel>>? RecipeGroups = null) : Table {
    // SalaryType 0 is paid in mesos, others in merets (MaidInfo, the maid item tooltip).
    public record Entry(int MaidId, int RecipeGroupId, int NpcId, int SalaryType = 0, int Salary = 0);

    // An ingredient: an item id, or an item tag (Tag != None, ItemId 0) that any item with that tag fills.
    public record Ingredient(int ItemId, ItemTag Tag, int Count);

    // A recipe (CMaidRecipe). LeadTimes are seconds by mood grade (normal, good, very good); WorkbenchType > 0 is
    // the icon code of a cube the crafter's account must have placed in the field; ProductRank is 1..6.
    public record Recipe(
        int Id,
        IReadOnlyList<Ingredient> Ingredients,
        int WorkbenchType,
        int[] LeadTimes,
        int MaidExp,
        int MaidMood,
        int ProductItemId,
        int ProductCount,
        int ProductRank);

    // A recipe of a group with the affinity level it needs.
    public record RecipeLevel(int RecipeId, int RequireLevel);

    // A <recipe/> row read through attr (an absent attribute reads as ""), as CMaidRecipeTable::Load does. Null when
    // the id is not positive or an ingredient names a tag the server does not know (the server could not check it).
    // An ingredient item that is neither a positive id nor a tag name is left out, as the client leaves it out, and
    // so is an ingredient whose count is not positive: it asks for nothing (the ReduceMaidRecipe floor of 1 applies
    // to a counted ingredient only). The product count is at least 1.
    public static Recipe? RecipeRow(Func<string, string> attr) {
        if (!int.TryParse(attr("Id"), out int id) || id <= 0) {
            return null;
        }
        var ingredients = new List<Ingredient>();
        foreach (string prefix in new[] { "First", "Second", "Third" }) {
            string item = attr(prefix + "IngredientItemID").Trim();
            int.TryParse(attr(prefix + "IngredientCount"), out int count);
            if (uint.TryParse(item, out uint itemId)) {
                if (itemId > 0 && count > 0) {
                    ingredients.Add(new Ingredient((int) itemId, ItemTag.None, count));
                }
                continue;
            }
            if (item.Length == 0) {
                continue;
            }
            if (!System.Enum.TryParse(item, out ItemTag tag) || tag == ItemTag.None || int.TryParse(item, out _)) {
                return null;
            }
            if (count > 0) {
                ingredients.Add(new Ingredient(0, tag, count));
            }
        }
        int.TryParse(attr("WorkbenchType"), out int workbench);
        int.TryParse(attr("LeadTimeNormal"), out int normal);
        int.TryParse(attr("LeadTimeGood"), out int good);
        int.TryParse(attr("LeadTimeVeryGood"), out int veryGood);
        int.TryParse(attr("MaidExp"), out int exp);
        int.TryParse(attr("MaidMood"), out int mood);
        int.TryParse(attr("ProductItemID"), out int product);
        int.TryParse(attr("ProductCount"), out int productCount);
        int.TryParse(attr("ProductRank"), out int rank);
        return new Recipe(id, ingredients, workbench, [normal, good, veryGood], exp, mood, product, Math.Max(1, productCount), Math.Clamp(rank, 1, 6));
    }

    // A <group GroupID RecipeIDs RequireLevels/> row (comma lists), sorted by level, then recipe id. Null when the
    // two lists differ in length: the client then fails the whole table (CMaidRecipeGroupTable::Load).
    public static IReadOnlyList<RecipeLevel>? GroupRow(string recipeIds, string requireLevels) {
        List<int> ids = CommaInts(recipeIds);
        List<int> levels = CommaInts(requireLevels);
        if (ids.Count != levels.Count) {
            return null;
        }
        return ids.Zip(levels, (recipe, level) => new RecipeLevel(recipe, level))
            .OrderBy(entry => entry.RequireLevel)
            .ThenBy(entry => entry.RecipeId)
            .ToList();
    }

    private static List<int> CommaInts(string value) {
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out int number) ? number : 0)
            .ToList();
    }

    // A <Property MaidID RecipeGroupID NPCID/> row; null when the maid or npc id is not a positive number.
    public static Entry? Row(string maidId, string recipeGroupId, string npcId) {
        if (!int.TryParse(maidId, out int maid) || maid <= 0 || !int.TryParse(npcId, out int npc) || npc <= 0) {
            return null;
        }
        int.TryParse(recipeGroupId, out int recipeGroup);
        return new Entry(maid, recipeGroup, npc);
    }
}
