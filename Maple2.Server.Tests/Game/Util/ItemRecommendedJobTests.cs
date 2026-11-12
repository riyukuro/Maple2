using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The job an item's option values are scaled for, as the client's ItemOptionValueRef::GetRecommendedJob picks it.
public class ItemRecommendedJobTests {
    private static ItemMetadataLimit Limit(JobCode[] limits, JobCode[] recommends) =>
        new(Gender.All, 0, TransferType.Tradable, 0, false, false, false, false, false, false, false, 0, limits, recommends);

    [Test]
    public void RecommendedJob_ComesFromTheRecommendedJobsNotTheLimit() {
        // A weapon with no job limit that recommends the rune blader (15400118).
        Assert.That(ItemStatsCalculator.RecommendedJob(Limit([], [JobCode.RuneBlader])), Is.EqualTo(90));
        Assert.That(ItemStatsCalculator.RecommendedJob(Limit([JobCode.Knight], [])), Is.EqualTo(0));
    }

    [Test]
    public void RecommendedJob_SkipsNoneAndBeginnerAndTakesTheLowest() {
        Assert.That(ItemStatsCalculator.RecommendedJob(Limit([], [JobCode.None, JobCode.Newbie])), Is.EqualTo(0));
        Assert.That(ItemStatsCalculator.RecommendedJob(Limit([], [JobCode.Newbie, JobCode.Striker, JobCode.Berserker])), Is.EqualTo(20));
    }

    [Test]
    public void RecommendsManyJobs_CountsDistinctJobs() {
        Assert.That(ItemStatsCalculator.RecommendsManyJobs(Limit([], [JobCode.Striker])), Is.False);
        Assert.That(ItemStatsCalculator.RecommendsManyJobs(Limit([], [JobCode.Striker, JobCode.Striker])), Is.False);
        Assert.That(ItemStatsCalculator.RecommendsManyJobs(Limit([], [JobCode.None, JobCode.Striker])), Is.True);
    }

    // The intelligence of a striker item: only one that recommends more jobs gets the mage value.
    [Test]
    public void ConstantInt_ForTheStriker_NeedsManyJobs() {
        (float single, _) = Maple2.Server.Game.LuaFunctions.Lua.ConstantValueInt(0, 0, 10, 100, 0, 1, 50, 0);
        (float many, _) = Maple2.Server.Game.LuaFunctions.Lua.ConstantValueInt(0, 0, 10, 100, 0, 1, 50, 1);
        Assert.That(single, Is.EqualTo(0));
        Assert.That(many, Is.EqualTo(10));
    }
}
