using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;

namespace Maple2.Server.Tests.Game.Manager;

// A rolled individual drop box group (ItemDropManager.PickGroupEntries), on a box shaped like KR room box 21001001:
// group 1 always gives one smart drop entry (40 entries, every job's gear, no weights), groups 2 and 3 give one entry
// at 500 in 10000 (10 smart drop entries without weights; 5 entries of weight 10000). The dungeon clear gave all 55.
public class IndividualDropPickTests {
    private const int JobItemOffset = 7;

    private static IndividualDropItemTable.Item Entry(int id, int weight) =>
        new([id, 0], false, 1, 0, weight, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);

    // A row with the plain weight only (no job weights).
    private static IndividualDropItemTable.Item Plain(int id, int weight) =>
        new([id, 0], false, 0, 0, weight, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);

    private static IndividualDropItemTable.Entry Group(int groupId, int smartDropRate, IndividualDropItemTable.Entry.DropCount[] counts, IEnumerable<IndividualDropItemTable.Item> items) =>
        new(groupId, smartDropRate, counts, 0, false, false, items.ToList());

    private static readonly IndividualDropItemTable.Entry.DropCount[] Always = [new(1, 100)];
    private static readonly IndividualDropItemTable.Entry.DropCount[] Rare = [new(0, 9500), new(1, 500)];

    private static readonly IndividualDropItemTable.Entry[] Box = [
        Group(1, 100, Always, Enumerable.Range(0, 40).Select(i => Entry(13100000 + i, 0))),
        Group(2, 100, Rare, Enumerable.Range(0, 10).Select(i => Entry(13200000 + i, 0))),
        Group(3, 0, Rare, Enumerable.Range(0, 5).Select(i => Entry(11900000 + i, 10000))),
    ];

    // The player's job is the one of the entries ending in JobItemOffset.
    private static List<IndividualDropItemTable.Item> Roll() {
        return Box.SelectMany(group => ItemDropManager.PickGroupEntries(group, group.Items, _ => true,
            itemId => itemId % 100 == JobItemOffset, item => item.ProperJobWeight)).ToList();
    }

    [Test]
    public void RoomBox_GivesAtMostOneEntryPerGroup() {
        for (int i = 0; i < 2000; i++) {
            List<IndividualDropItemTable.Item> picked = Roll();
            Assert.That(picked, Has.Count.InRange(1, 3));
            Assert.That(picked.Count(item => item.Ids[0] / 100000 == 131), Is.EqualTo(1));
        }
    }

    [Test]
    public void SmartDropWithoutWeights_GivesThePlayersJobEntry() {
        for (int i = 0; i < 200; i++) {
            List<IndividualDropItemTable.Item> picked = ItemDropManager.PickGroupEntries(Box[0], Box[0].Items, _ => true,
                itemId => itemId % 100 == JobItemOffset, item => item.Ids[0] % 100 == JobItemOffset ? item.ProperJobWeight : item.ImproperJobWeight).ToList();
            Assert.That(picked.Select(item => item.Ids[0]), Is.EqualTo(new[] { 13100000 + JobItemOffset }));
        }
    }

    [Test]
    public void JobWeight_ByTheItemsRecommendedJobs() {
        JobCode[] knight = [JobCode.Knight];
        Assert.That(ItemDropManager.JobWeight(knight, JobCode.Knight, 23, 253, 0), Is.EqualTo(253));
        Assert.That(ItemDropManager.JobWeight(knight, JobCode.Striker, 23, 253, 0), Is.EqualTo(0));
        Assert.That(ItemDropManager.JobWeight(knight, JobCode.Striker, 18, 187, 18), Is.EqualTo(18));
        Assert.That(ItemDropManager.JobWeight([JobCode.None], JobCode.Striker, 18, 187, 5), Is.EqualTo(187));
        Assert.That(ItemDropManager.JobWeight([], JobCode.Striker, 18, 187, 5), Is.EqualTo(18));
    }

    // One group per job (KR box 320000497): the group of another job gives nothing, the own job's group its row.
    [Test]
    public void SmartDropGroupOfAnotherJob_GivesNothing() {
        IndividualDropItemTable.Item row = Entry(20000001, 100);
        IndividualDropItemTable.Entry group = Group(1, 100, Always, [row]);
        JobCode[] knight = [JobCode.Knight];
        Assert.That(ItemDropManager.PickGroupEntries(group, group.Items, _ => true, _ => false,
            item => ItemDropManager.JobWeight(knight, JobCode.Striker, item.Weight, item.ProperJobWeight, item.ImproperJobWeight)), Is.Empty);
        Assert.That(ItemDropManager.PickGroupEntries(group, group.Items, _ => true, _ => true,
            item => ItemDropManager.JobWeight(knight, JobCode.Knight, item.Weight, item.ProperJobWeight, item.ImproperJobWeight)).ToList(), Has.Count.EqualTo(1));
    }

    // KR box 330002235 group 1: smart drop rate 100, rows with a plain weight and no job weights.
    [Test]
    public void SmartDropWithPlainWeightsOnly_RollsByWeight() {
        IndividualDropItemTable.Entry group = Group(1, 100, Always, [Plain(20000001, 100), Plain(20000002, 29), Plain(20000003, 0)]);
        var seen = new Dictionary<int, int>();
        for (int i = 0; i < 4000; i++) {
            List<IndividualDropItemTable.Item> picked = ItemDropManager.PickGroupEntries(group, group.Items, _ => true,
                _ => false, item => item.ImproperJobWeight).ToList();
            Assert.That(picked, Has.Count.EqualTo(1));
            seen[picked[0].Ids[0]] = seen.GetValueOrDefault(picked[0].Ids[0]) + 1;
        }
        Assert.That(seen.ContainsKey(20000003), Is.False);
        Assert.That(seen[20000001], Is.GreaterThan(seen[20000002] * 2));
        Assert.That(seen[20000002], Is.GreaterThan(400));
    }

    // KR spawn box 23000080 group 2: smart drop rate 30, rows with both weights (11300596: weight 18, proper 187,
    // improper 18). The client takes the job weights for a roll only at the rate (GetRandomRange(100, 0) < 30), the
    // plain weight the rest; a group at 100 always takes the job weights.
    [Test]
    public void SmartDropRateUnder100_RollsThePlainWeightsTheRest() {
        IndividualDropItemTable.Item plainOnly = new([20000001, 0], false, 0, 0, 100, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);
        IndividualDropItemTable.Item jobOnly = new([20000002, 0], false, 100, 0, 0, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);
        int Plains(int rate) {
            IndividualDropItemTable.Entry group = Group(2, rate, Always, [plainOnly, jobOnly]);
            int plain = 0;
            for (int i = 0; i < 2000; i++) {
                List<IndividualDropItemTable.Item> picked = ItemDropManager.PickGroupEntries(group, group.Items, _ => true,
                    _ => true, item => item.ProperJobWeight).ToList();
                Assert.That(picked, Has.Count.EqualTo(1));
                plain += picked[0].Ids[0] == 20000001 ? 1 : 0;
            }
            return plain;
        }

        Assert.That(Plains(30), Is.InRange(1250, 1550));
        Assert.That(Plains(100), Is.EqualTo(0));
    }

    [Test]
    public void SmartDropWithoutAJobEntry_GivesNothing() {
        Assert.That(ItemDropManager.PickGroupEntries(Box[0], Box[0].Items, _ => true, _ => false, item => item.ImproperJobWeight), Is.Empty);
    }

    [Test]
    public void NoCountRolled_GivesNothing() {
        IndividualDropItemTable.Entry never = Group(4, 0, [new(0, 100)], [Entry(11900001, 10000)]);
        Assert.That(ItemDropManager.PickGroupEntries(never, never.Items, _ => true, _ => true, _ => 1), Is.Empty);
    }

    [Test]
    public void WeightedGroup_GivesTheCountRolled() {
        IndividualDropItemTable.Entry three = Group(5, 0, [new(3, 100)], Enumerable.Range(0, 5).Select(i => Entry(11900000 + i, 10000)));
        Assert.That(ItemDropManager.PickGroupEntries(three, three.Items, _ => true, _ => true, _ => 1).Count(), Is.EqualTo(3));
        Assert.That(ItemDropManager.PickGroupEntries(three, three.Items, _ => false, _ => true, _ => 1), Is.Empty);
    }

    // Boxes rolled without a player (trigger item spawns). KR spawn box 390000015: four potion groups at 100%, a fifth
    // group at 50 in 10000; the spawn gave all five every time.
    [Test]
    public void SpawnBox_RareGroupIsRolled() {
        IndividualDropItemTable.Entry[] box = [
            .. Enumerable.Range(1, 4).Select(g => Group(g, 0, Always, [Entry(20000016 + g, 10000)])),
            Group(5, 0, [new(0, 9950), new(1, 50)], [Entry(90000004, 10000)]),
        ];
        int rare = 0;
        for (int i = 0; i < 2000; i++) {
            List<IndividualDropItemTable.Item> picked = ItemDropManager.PickBoxEntriesWithoutPlayer(box, 0).ToList();
            Assert.That(picked.Count(item => item.Ids[0] != 90000004), Is.EqualTo(4));
            rare += picked.Count - 4;
        }
        Assert.That(rare, Is.LessThan(100));
    }

    // KR spawn box 23000080 (two field maps): smart drop gear groups of 88 and 127 entries with weights at 100%, two
    // rare gear groups; the spawn gave all 236 entries.
    [Test]
    public void SpawnGearBox_GivesOneEntryPerRolledGroup() {
        IndividualDropItemTable.Entry[] box = [
            Group(2, 30, Always, Enumerable.Range(0, 88).Select(i => Entry(11300000 + i, 18))),
            Group(3, 30, Always, Enumerable.Range(0, 127).Select(i => Entry(11400000 + i, 100))),
            Group(4, 100, [new(0, 7000), new(1, 3000)], Enumerable.Range(0, 11).Select(i => Entry(11600000 + i, 23))),
            Group(5, 100, [new(0, 8800), new(1, 1200)], Enumerable.Range(0, 8).Select(i => Entry(11900000 + i, 116))),
        ];
        for (int i = 0; i < 500; i++) {
            Assert.That(ItemDropManager.PickBoxEntriesWithoutPlayer(box, 0).Count(), Is.InRange(2, 4));
        }
    }

    [Test]
    public void SpawnBox_SkipsQuestAndOtherMapEntries() {
        IndividualDropItemTable.Item quest = Entry(30000001, 10000) with { QuestConstraint = true };
        IndividualDropItemTable.Item otherMap = Entry(30000002, 10000) with { MapIds = [2000001] };
        IndividualDropItemTable.Item here = Entry(30000003, 10000) with { MapIds = [2000002] };
        IndividualDropItemTable.Entry[] box = [Group(1, 0, Always, [quest, otherMap, here])];
        for (int i = 0; i < 100; i++) {
            Assert.That(ItemDropManager.PickBoxEntriesWithoutPlayer(box, 2000002).Select(item => item.Ids[0]), Is.EqualTo(new[] { 30000003 }));
        }
        Assert.That(ItemDropManager.PickBoxEntriesWithoutPlayer(box, 2000001).Select(item => item.Ids[0]), Is.EqualTo(new[] { 30000002 }));
    }

    // Interact objects. KR box 310000018 gives its one entry at 3000 in 10000 (the object gave it every time);
    // 310000226 has three meso groups at 100% (the object gave one of them).
    [Test]
    public void InteractBox_RolledCountAndEveryGroup() {
        IndividualDropItemTable.Entry[] chance = [Group(1, 0, [new(0, 7000), new(1, 3000)], [Entry(30000022, 10000)])];
        int given = Enumerable.Range(0, 2000).Sum(_ => ItemDropManager.PickBoxEntriesWithoutPlayer(chance, 0).Count());
        Assert.That(given, Is.InRange(450, 750));

        IndividualDropItemTable.Entry[] meso = [.. Enumerable.Range(1, 3).Select(g => Group(g, 0, Always, [Entry(90000001, 10000)]))];
        Assert.That(meso.SelectMany(group => ItemDropManager.PickGroupEntries(group, group.Items, _ => true, _ => true, item => item.Weight)).Count(), Is.EqualTo(3));
    }
}
