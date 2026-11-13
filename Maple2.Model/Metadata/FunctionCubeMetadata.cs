using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Model.Metadata;

public record FunctionCubeMetadata(
    int Id,
    int RecipeId,
    ConfigurableCubeType ConfigurableCubeType,
    InteractCubeState DefaultState,
    InteractCubeControlType ControlType,
    int[] AutoStateChange,
    int AutoStateChangeTime,
    FunctionCubeMetadata.NurturingData? Nurturing
) {
    public record NurturingData(
        RewardItem Feed,
        RewardItem RewardFeed,
        NurturingData.Growth[] RequiredGrowth,
        string QuestTag
    ) {
        public record Growth(int Exp, short Stage, RewardItem Reward);

        // The pet npc of each stage (pluralNpcID, client GetPetNpcId by level). The nurturing trophies name the
        // stage-1 npc as their code (92000095 "Play with ... in someone's home": code 50400282).
        public int[] PetNpcIds { get; init; } = [];

        // The code the nurturing trophies count for: the stage-1 pet npc, else the cube's item id (each cube item's
        // stage-1 npc has the item's id, e.g. 50400309).
        public int TrophyCode(int itemId) => PetNpcIds.Length > 0 ? PetNpcIds[0] : itemId;
    }

    // A home mannequin (UseUiFunction FittingDoll): the doll's gender and type, 1 for an outfit (skin) doll and 0 for
    // an equipment doll (client ObjectData m_cube.m_fittingDoll, commonfiles/objectdata.cpp:236). Null for any other cube.
    public FittingDollData? FittingDoll { get; init; }

    public record FittingDollData(Gender Gender, int Type);
}
