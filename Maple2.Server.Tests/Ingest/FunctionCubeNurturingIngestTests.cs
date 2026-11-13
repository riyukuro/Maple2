using System.Linq;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Nurturing = Maple2.File.Parser.Xml.Object.Nurturing;

namespace Maple2.Server.Tests.Ingest;

// Client: ObjectData (commonfiles/objectdata.cpp:321-378) takes rewardItem and rewardItemByFeeding only as 3 values
// and leaves item 0 otherwise; the cube is a pet by its ControlType Nurturing (IsNurturingControlType).
public class FunctionCubeNurturingIngestTests {
    // Object 50800002: no rewardItemByFeeding, still a pet.
    private static Nurturing PumpkinDevil() => new() {
        pluralNpcID = [50400309, 50400310, 50400311],
        rewardItem = [20302004, 1, 1],
        rewardItemByFeeding = [],
        requiredGrowth = [300, 500, 700],
        rewardItemByGrowth = [59400069, 1, 1, 50600279, 3, 1, 11301282, 1, 1],
        nurturingQuestTag = "NurturingPumpkinDevil",
    };

    [Test]
    public void NoFeedingReward_IsStillNurturing() {
        FunctionCubeMetadata.NurturingData? data = FunctionCubeMapper.ParseNurturing(InteractCubeControlType.Nurturing, PumpkinDevil());
        Assert.That(data, Is.Not.Null);
        Assert.That(data!.Feed.ItemId, Is.EqualTo(20302004));
        Assert.That(data.RewardFeed.ItemId, Is.EqualTo(0));
        Assert.That(data.RequiredGrowth.Select(x => x.Exp), Is.EqualTo(new[] { 300, 500, 700 }));
        Assert.That(data.RequiredGrowth[2].Reward.ItemId, Is.EqualTo(11301282));
        Assert.That(data.TrophyCode(50400309), Is.EqualTo(50400309));
    }

    [Test]
    public void OtherControlType_OrNoStages_IsNotNurturing() {
        Assert.That(FunctionCubeMapper.ParseNurturing(InteractCubeControlType.None, PumpkinDevil()), Is.Null);
        Assert.That(FunctionCubeMapper.ParseNurturing(InteractCubeControlType.Nurturing, new Nurturing()), Is.Null);
    }

    // A list of other than 3 values is item 0, as in the client.
    [Test]
    public void ShortRewardList_IsItemZero() {
        Nurturing nurturing = PumpkinDevil();
        nurturing.rewardItem = [20302004, 1];
        FunctionCubeMetadata.NurturingData? data = FunctionCubeMapper.ParseNurturing(InteractCubeControlType.Nurturing, nurturing);
        Assert.That(data!.Feed.ItemId, Is.EqualTo(0));
    }
}
