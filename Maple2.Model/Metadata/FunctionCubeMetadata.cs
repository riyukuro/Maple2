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
    }

    // A home mannequin (UseUiFunction FittingDoll): the doll's gender and type, 1 for an outfit (skin) doll and 0 for
    // an equipment doll (client ObjectData m_cube.m_fittingDoll, commonfiles/objectdata.cpp:236). Null for any other cube.
    public FittingDollData? FittingDoll { get; init; }

    public record FittingDollData(Gender Gender, int Type);
}
