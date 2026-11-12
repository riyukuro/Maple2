using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;

namespace Maple2.Server.Tests.Game.Manager;

// A rolled individual drop box group (ItemDropManager.PickGroupEntries), on a box shaped like KR room box 21001001:
// group 1 always gives one smart drop entry (40 entries, every job's gear, no weights), groups 2 and 3 give one entry
// at 500 in 10000 (10 smart drop entries without weights; 5 entries of weight 10000). The dungeon clear gave all 55.
public class IndividualDropPickTests {
    private const int JobItemOffset = 7;

    private static IndividualDropItemTable.Item Entry(int id, int weight) =>
        new([id, 0], false, 1, 0, weight, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], 0);

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
                itemId => itemId % 100 == JobItemOffset, item => item.ProperJobWeight).ToList();
            Assert.That(picked.Select(item => item.Ids[0]), Is.EqualTo(new[] { 13100000 + JobItemOffset }));
        }
    }

    [Test]
    public void SmartDropWithoutAJobEntry_GivesNothing() {
        Assert.That(ItemDropManager.PickGroupEntries(Box[0], Box[0].Items, _ => true, _ => false, item => item.ProperJobWeight), Is.Empty);
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
}
