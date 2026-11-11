using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

// Skills (weddingskill.xml) and Grades (weddingexp.xml: grade -> exp) are null in metadata ingested before they
// were added; a new ingest fills them.
public record WeddingTable(IReadOnlyDictionary<MarriageExpType, WeddingReward> Rewards,
                                 IReadOnlyDictionary<int, WeddingPackage> Packages,
                                 IReadOnlyDictionary<int, WeddingSkill>? Skills = null,
                                 IReadOnlyDictionary<int, long>? Grades = null) : Table;

// A couple skill unlocked by the wedding grade. The client's types: passive (an additional effect whose level
// grows with the grade) and item (an item the couple can take from the skill page).
public record WeddingSkill(
    int Id,
    WeddingSkillType Type,
    int RequireGrade,
    int ItemId,
    int ItemRarity,
    int ItemAmount);

public enum WeddingSkillType {
    Passive = 0,
    Item = 1,
}

public record WeddingReward(
    MarriageExpType Type,
    int Amount,
    MarriageExpLimit Limit);

public record WeddingPackage(
    int Id,
    int PlannerId,
    IReadOnlyDictionary<int, WeddingPackage.HallData> Halls) {
    public record HallData(
        int Id,
        int MapId,
        int NightMapId,
        int Tier,
        int MeretCost,
        IReadOnlyList<HallData.Item> Items,
        IReadOnlyList<HallData.Item> CompleteItems) {

        public record Item(
            int ItemId,
            short Rarity,
            int Amount,
            bool NightOnly);
    }
}
