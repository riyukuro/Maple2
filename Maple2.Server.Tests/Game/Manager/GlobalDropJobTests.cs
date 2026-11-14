using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Manager;

// The job filter on a global drop (ItemDropTable::DropGlobal): rolled for a player, an item recommended only for
// other jobs is not a candidate. KR box 1 group 1 lists gear such as 14000037 (Priest) and 11700132 (Wizard,
// Priest) beside every-job gear such as 11400052; in the KMS2 field-play data each of the 106 job-recommended gear
// drops from that group was for the looter's job.
public class GlobalDropJobTests {
    private const int DropBoxId = 1;
    private const int GroupId = 1;
    private const int PriestItem = 14000037;
    private const int WizardPriestItem = 11700132;
    private const int KnightItem = 13000002;
    private const int EveryJobItem = 11400052;

    private TestGameSession test = null!;

    private void SetUp(params (int Id, JobCode[] Recommends)[] items) {
        test = new TestGameSession();
        foreach ((int id, JobCode[] recommends) in items) {
            ItemMetadata data = TestGameSession.Metadata(id);
            test.Register(data with { Limit = data.Limit with { JobRecommends = recommends } });
        }
        var group = new GlobalDropItemBoxTable.Group(GroupId, 0, 0, [new GlobalDropItemBoxTable.Group.DropCount(1, 100)], true, (MapType) 0,
            (Continent) 0);
        IList<GlobalDropItemBoxTable.Item> rows = items.Select(item =>
            new GlobalDropItemBoxTable.Item(item.Id, 1, 99, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1, [], false)).ToList();
        test.SetServerTable(new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [DropBoxId] = new() { [GroupId] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> { [GroupId] = rows }));
        test.Start();
    }

    private HashSet<int> Drops(Job job, int rolls = 200) {
        test.Session.Player.Value.Character.Job = job;
        var ids = new HashSet<int>();
        for (int i = 0; i < rolls; i++) {
            ids.UnionWith(test.Session.Field!.ItemDrop.GetGlobalDropItems(DropBoxId, 39, test.Session).Select(item => item.Id));
        }
        return ids;
    }

    [Test]
    public void APlayersRoll_LeavesOutOtherJobsGear() {
        SetUp((PriestItem, [JobCode.Priest]), (WizardPriestItem, [JobCode.Wizard, JobCode.Priest]), (KnightItem, [JobCode.Knight]),
            (EveryJobItem, [JobCode.None]));

        Assert.That(Drops(Job.PriestII), Is.EquivalentTo(new[] { PriestItem, WizardPriestItem, EveryJobItem }));
        Assert.That(Drops(Job.Wizard), Is.EquivalentTo(new[] { WizardPriestItem, EveryJobItem }));
    }

    // ItemLimit::IsJobRecommended counts a beginner as a knight.
    [Test]
    public void ABeginner_GetsKnightGear() {
        SetUp((PriestItem, [JobCode.Priest]), (KnightItem, [JobCode.Knight]), (EveryJobItem, [JobCode.None]));

        Assert.That(Drops(Job.Newbie), Is.EquivalentTo(new[] { KnightItem, EveryJobItem }));
    }

    // Only other jobs' gear in the group: nothing drops (the roll is not spent on an item the player cannot use).
    [Test]
    public void OnlyOtherJobsGear_DropsNothing() {
        SetUp((PriestItem, [JobCode.Priest]));

        Assert.That(Drops(Job.Knight, rolls: 20), Is.Empty);
    }

    // A roll without a player (a trigger spawn or a dungeon reward roll) keeps every job's gear.
    [Test]
    public void NoPlayer_KeepsEveryJob() {
        SetUp((PriestItem, [JobCode.Priest]), (KnightItem, [JobCode.Knight]));
        var ids = new HashSet<int>();
        for (int i = 0; i < 200; i++) {
            ids.UnionWith(test.Session.Field!.ItemDrop.GetGlobalDropItems(DropBoxId, 39).Select(item => item.Id));
        }

        Assert.That(ids, Is.EquivalentTo(new[] { PriestItem, KnightItem }));
    }
}
