using Maple2.File.IO;
using Maple2.File.Parser;
using Maple2.File.Parser.Xml.Object;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Nurturing = Maple2.File.Parser.Xml.Object.Nurturing;

namespace Maple2.File.Ingest.Mapper;

public class FunctionCubeMapper : TypeMapper<FunctionCubeMetadata> {
    private readonly FunctionCubeParser parser;

    public FunctionCubeMapper(M2dReader xmlReader) {
        parser = new FunctionCubeParser(xmlReader);
    }

    protected override IEnumerable<FunctionCubeMetadata> Map() {
        foreach ((int id, FunctionCubeRoot functionCubeRoot) in parser.Parse()) {
            FunctionCube functionCube = functionCubeRoot.FunctionCube;

            ConfigurableCube? configurableCube = functionCubeRoot.ConfigurableCube;
            InteractCubeControlType controlType = Enum.TryParse(functionCube.ControlType, out InteractCubeControlType parsed) ? parsed : InteractCubeControlType.None;
            yield return new FunctionCubeMetadata(
                Id: id,
                RecipeId: functionCube.receipeID,
                ConfigurableCubeType: configurableCube is not null ? (ConfigurableCubeType) configurableCube.id : ConfigurableCubeType.None,
                DefaultState: (InteractCubeState) functionCube.DefaultState,
                ControlType: controlType,
                AutoStateChange: functionCube.AutoStateChange,
                AutoStateChangeTime: functionCube.AutoStateChangeTime,
                Nurturing: ParseNurturing(controlType, functionCube.nurturing)
            ) {
                // Every cube parses with an empty doll node; only the mannequins open the doll window.
                FittingDoll = functionCube.UseUiFunction.ToString() == "FittingDoll" && functionCube.fittingdoll is { } doll
                    ? new FunctionCubeMetadata.FittingDollData((Gender) (int) doll.fittingdollGender, doll.fittingdollType)
                    : null,
            };
        }
    }

    // Client: ObjectData (commonfiles/objectdata.cpp:321-378) reads the nurturing node of any object; the cube is a
    // pet when its ControlType is Nurturing (0xd, IsNurturingControlType). rewardItem and rewardItemByFeeding are
    // taken only as a list of exactly 3 (id, rarity, amount) and stay item 0 otherwise: 50400283, 50800001/2/3/5/6
    // have no rewardItemByFeeding and are still pets. Every object parses with an empty node, so a cube of another
    // control type, or one without growth stages, has no nurturing.
    public static FunctionCubeMetadata.NurturingData? ParseNurturing(InteractCubeControlType controlType, Nurturing? functionCubeNurturing) {
        if (controlType != InteractCubeControlType.Nurturing || functionCubeNurturing is null) {
            return null;
        }
        FunctionCubeMetadata.NurturingData.Growth[] growth = ParseRequiredGrowth(functionCubeNurturing);
        if (growth.Length == 0) {
            return null;
        }
        return new FunctionCubeMetadata.NurturingData(
            Feed: ParseReward(functionCubeNurturing.rewardItem),
            RewardFeed: ParseReward(functionCubeNurturing.rewardItemByFeeding),
            RequiredGrowth: growth,
            QuestTag: functionCubeNurturing.nurturingQuestTag ?? string.Empty
        ) {
            PetNpcIds = functionCubeNurturing.pluralNpcID ?? [],
        };

        static RewardItem ParseReward(int[]? values) {
            return values is { Length: 3 } ? new RewardItem(itemId: values[0], rarity: (short) values[1], amount: values[2]) : new RewardItem(0, 0, 0);
        }

        // A stage needs its growth and its 3 reward values (requiredGrowth and rewardItemByGrowth, read by stage).
        static FunctionCubeMetadata.NurturingData.Growth[] ParseRequiredGrowth(Nurturing nurturing) {
            int[] required = nurturing.requiredGrowth ?? [];
            int[] rewards = nurturing.rewardItemByGrowth ?? [];
            List<FunctionCubeMetadata.NurturingData.Growth> result = [];
            for (int i = 0; i + 2 < rewards.Length && i / 3 < required.Length; i += 3) {
                result.Add(new FunctionCubeMetadata.NurturingData.Growth(
                    Exp: required[i / 3],
                    Stage: (short) (i / 3 + 1),
                    Reward: new RewardItem(
                        itemId: rewards[i],
                        rarity: (short) rewards[i + 1],
                        amount: rewards[i + 2]
                    )
                ));
            }
            return result.ToArray();
        }
    }
}
