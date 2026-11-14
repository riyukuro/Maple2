using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game;

// ItemLimit::IsJobAllowed (itemdatastruct.cpp:690) with the item's disableJobLimit: a job named there is refused
// before the beginner counts as a knight, and whatever the job list allows. KR's 7 items with the list
// (item boxes such as 20300160) name the beginner and have an empty job list. The item id is made up.
public class ItemJobLimitTests {
    private static ItemMetadataLimit Limit(JobCode[] jobs, JobCode[]? disabled) =>
        TestGameSession.Metadata(20000001).Limit with { JobLimits = jobs, DisableJobLimits = disabled };

    [TestCase(new JobCode[0], null, JobCode.Newbie, ExpectedResult = true)]
    [TestCase(new JobCode[0], new[] { JobCode.Newbie }, JobCode.Newbie, ExpectedResult = false)]
    [TestCase(new JobCode[0], new[] { JobCode.Newbie }, JobCode.Knight, ExpectedResult = true)]
    [TestCase(new[] { JobCode.Knight }, new[] { JobCode.Newbie }, JobCode.Newbie, ExpectedResult = false)]
    [TestCase(new[] { JobCode.Knight }, new[] { JobCode.Knight }, JobCode.Knight, ExpectedResult = false)]
    [TestCase(new[] { JobCode.Newbie }, new[] { JobCode.Wizard }, JobCode.Wizard, ExpectedResult = false)]
    [TestCase(new[] { JobCode.Newbie }, new[] { JobCode.Wizard }, JobCode.Archer, ExpectedResult = true)]
    [TestCase(new[] { JobCode.Knight }, null, JobCode.Newbie, ExpectedResult = true)]
    [TestCase(new[] { JobCode.Knight }, null, JobCode.None, ExpectedResult = false)]
    public bool JobAllowed_TheDisableListComesFirst(JobCode[] jobs, JobCode[]? disabled, JobCode job) =>
        ItemUseHandler.JobAllowed(Limit(jobs, disabled), job);

    [TestCase(Job.Newbie, ExpectedResult = false)]
    [TestCase(Job.Knight, ExpectedResult = true)]
    public bool Use_ABeginnerCannotUseAnItemThatDisablesTheBeginner(Job job) {
        var test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(20000001);
        Item stored = test.Store(metadata with { Limit = metadata.Limit with { JobLimits = [], DisableJobLimits = [JobCode.Newbie] } });
        test.Start();
        test.Session.Player.Value.Character.Job = job;
        return ItemUseHandler.UseAllowed(test.Session, test.Session.Item.Inventory.Get(stored.Uid)!);
    }
}
