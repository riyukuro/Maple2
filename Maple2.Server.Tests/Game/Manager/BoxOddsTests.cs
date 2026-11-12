using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Tools;

namespace Maple2.Server.Tests.Game.Manager;

// The odds of the item boxes, rolled with a seeded random: random boxes (OpenItemBox), key boxes (OpenItemBoxWithKey)
// and capsules (OpenGachaBox) all roll an individual drop box through ItemDropManager.PickGroupEntries, RollAmount and
// RollRarity; a select box gives the picked rows of ItemDropManager.SelectBoxRows and rolls only count and rank.
public class BoxOddsTests {
    private const int Rolls = 20000;

    private static IndividualDropItemTable.Item Entry(int id, int weight, int proper = 0, int improper = 0) =>
        new([id, 0], false, proper, improper, weight, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], 0);

    private static IndividualDropItemTable.Entry Group(int groupId, int smartDropRate, IndividualDropItemTable.Entry.DropCount[] counts, params IndividualDropItemTable.Item[] items) =>
        new(groupId, smartDropRate, counts, 0, false, false, items.ToList());

    private static readonly IndividualDropItemTable.Entry.DropCount[] One = [new(1, 100)];

    // The job of an item id is its last digit; the player is job 3.
    private static bool Mine(int itemId) => itemId % 10 == 3;
    private static int JobWeight(IndividualDropItemTable.Item item) => Mine(item.Ids[0]) ? item.ProperJobWeight : item.ImproperJobWeight;

    private static Dictionary<int, int> Tally(IndividualDropItemTable.Entry group, Random random, Func<IndividualDropItemTable.Item, bool>? available = null) {
        var tally = new Dictionary<int, int>();
        for (int i = 0; i < Rolls; i++) {
            foreach (IndividualDropItemTable.Item item in ItemDropManager.PickGroupEntries(group, group.Items, available ?? (_ => true), Mine, JobWeight, random)) {
                tally[item.Ids[0]] = tally.GetValueOrDefault(item.Ids[0]) + 1;
            }
        }
        return tally;
    }

    private static void AssertShare(int count, int total, double expected) {
        Assert.That((double) count / total, Is.EqualTo(expected).Within(0.02));
    }

    [Test]
    public void WeightedSet_SharesFollowTheWeights() {
        var set = new WeightedSet<int>(new Random(1));
        set.Add(1, 10);
        set.Add(2, 30);
        set.Add(3, 60);
        set.Add(4, 0);
        int[] hits = new int[5];
        for (int i = 0; i < Rolls; i++) {
            hits[set.Get()]++;
        }
        AssertShare(hits[1], Rolls, 0.10);
        AssertShare(hits[2], Rolls, 0.30);
        AssertShare(hits[3], Rolls, 0.60);
        Assert.That(hits[4], Is.Zero);
    }

    [Test]
    public void WeightedSet_ARepeatedRowCountsTwice() {
        var set = new WeightedSet<int>(new Random(2));
        set.Add(1, 50);
        set.Add(1, 50);
        set.Add(2, 100);
        Assert.That(set.Count, Is.EqualTo(3));
        int ones = Enumerable.Range(0, Rolls).Count(_ => set.Get() == 1);
        AssertShare(ones, Rolls, 0.50);
    }

    [Test]
    public void WeightedSet_ANegativeWeightIsNeverPicked() {
        var set = new WeightedSet<int>(new Random(3));
        set.Add(1, -500);
        set.Add(2, 100);
        Assert.That(Enumerable.Range(0, 1000).All(_ => set.Get() == 2), Is.True);

        var only = new WeightedSet<int>(new Random(3));
        only.Add(7, -1);
        Assert.That(only.Get(), Is.EqualTo(7));
    }

    [Test]
    public void SameSeed_SameRolls() {
        IndividualDropItemTable.Entry group = Group(1, 0, [new(1, 50), new(2, 50)], Entry(101, 10), Entry(102, 20), Entry(103, 70));
        int[] Run() {
            var random = new Random(77);
            return Enumerable.Range(0, 200).SelectMany(_ => ItemDropManager.PickGroupEntries(group, group.Items, _ => true, Mine, JobWeight, random))
                .Select(item => item.Ids[0]).ToArray();
        }
        Assert.That(Run(), Is.EqualTo(Run()));
    }

    // Random box: the entries' shares are their weights; an entry without a weight never drops next to weighted ones.
    [Test]
    public void RandomBox_EntrySharesFollowTheWeights() {
        IndividualDropItemTable.Entry group = Group(1, 0, One, Entry(101, 1000), Entry(102, 3000), Entry(103, 6000), Entry(104, 0));
        Dictionary<int, int> tally = Tally(group, new Random(4));
        Assert.That(tally.Values.Sum(), Is.EqualTo(Rolls));
        AssertShare(tally[101], Rolls, 0.10);
        AssertShare(tally[102], Rolls, 0.30);
        AssertShare(tally[103], Rolls, 0.60);
        Assert.That(tally.ContainsKey(104), Is.False);
    }

    // A group whose entries all have no weight (KR box 20002001 group 2) gives each entry as often.
    [Test]
    public void RandomBox_GroupWithoutWeightsIsEven() {
        IndividualDropItemTable.Entry group = Group(2, 0, One, Entry(101, 0), Entry(102, 0), Entry(103, 0), Entry(104, 0));
        Dictionary<int, int> tally = Tally(group, new Random(5));
        foreach (int id in new[] { 101, 102, 103, 104 }) {
            AssertShare(tally[id], Rolls, 0.25);
        }
    }

    // The guaranteed slot is a group whose only count row is 1; a rare slot rolls its count rows by probability.
    [Test]
    public void RandomBox_GuaranteedAndRareSlots() {
        IndividualDropItemTable.Entry sure = Group(1, 0, One, Entry(101, 100));
        IndividualDropItemTable.Entry rare = Group(2, 0, [new(0, 9500), new(1, 500)], Entry(201, 100));
        IndividualDropItemTable.Entry many = Group(3, 0, [new(1, 50), new(2, 30), new(3, 20)], Entry(301, 100));
        Assert.That(Tally(sure, new Random(6))[101], Is.EqualTo(Rolls));
        AssertShare(Tally(rare, new Random(6))[201], Rolls, 0.05);
        // 1 * 0.5 + 2 * 0.3 + 3 * 0.2 entries a roll
        Assert.That((double) Tally(many, new Random(6))[301] / Rolls, Is.EqualTo(1.7).Within(0.05));
    }

    // Smart drop with job weights (KR box 22000001: properJobWeight differs by entry, improperJobWeight 0): only the
    // player's job entries drop, by their proper weights.
    [Test]
    public void SmartBox_OnlyTheJobsEntriesByProperWeight() {
        IndividualDropItemTable.Entry group = Group(1, 100, One,
            Entry(1013, 10000, 130000), Entry(1023, 10000, 65000), Entry(1035, 10000, 130000), Entry(1047, 10000, 65000));
        Dictionary<int, int> tally = Tally(group, new Random(7));
        Assert.That(tally.Keys, Is.EquivalentTo(new[] { 1013, 1023 }));
        AssertShare(tally[1013], Rolls, 2.0 / 3);
    }

    // An improper weight lets another job's entry drop at that weight (KR box 22300105).
    [Test]
    public void SmartBox_ImproperWeightSharesWithOtherJobs() {
        IndividualDropItemTable.Entry group = Group(1, 100, One, Entry(1013, 10000, 130000), Entry(1025, 10000, 130000, 10000));
        Dictionary<int, int> tally = Tally(group, new Random(8));
        AssertShare(tally[1025], Rolls, 10000.0 / 140000);
    }

    // Smart drop with job weights only (KR box 20002001 group 1: 85 entries, 14 to 19 a job, properJobWeight 1, no
    // weight): every entry of the job drops, each as often. Only the first one in the table used to.
    [Test]
    public void SmartBox_JobWeightsOnly_EveryJobEntryDrops() {
        IndividualDropItemTable.Entry group = Group(1, 100, One,
            Entry(1013, 0, 1), Entry(1023, 0, 1), Entry(1033, 0, 1), Entry(1045, 0, 1), Entry(1055, 0, 1));
        Dictionary<int, int> tally = Tally(group, new Random(9));
        Assert.That(tally.Keys, Is.EquivalentTo(new[] { 1013, 1023, 1033 }));
        foreach (int count in tally.Values) {
            AssertShare(count, Rolls, 1.0 / 3);
        }
    }

    // Smart drop without any weight (KR box 21420101 group 4): one of the job's entries, each as often, once whatever
    // the count rolled; entries of a quest not in progress or of another map are left out.
    [Test]
    public void SmartBox_NoWeights_OneOfTheJobsEntries() {
        IndividualDropItemTable.Entry group = Group(1, 1, [new(3, 100)], Entry(1013, 0), Entry(1023, 0), Entry(1033, 0), Entry(1045, 0));
        Dictionary<int, int> tally = Tally(group, new Random(10));
        Assert.That(tally.Values.Sum(), Is.EqualTo(Rolls));
        Assert.That(tally.Keys, Is.EquivalentTo(new[] { 1013, 1023, 1033 }));
        foreach (int count in tally.Values) {
            AssertShare(count, Rolls, 1.0 / 3);
        }

        Dictionary<int, int> filtered = Tally(group, new Random(10), item => item.Ids[0] != 1013);
        Assert.That(filtered.Keys, Is.EquivalentTo(new[] { 1023, 1033 }));
    }

    // A job without an entry in a smart group gets nothing from it, not another job's entry.
    [Test]
    public void SmartBox_JobWithoutEntry_GivesNothing() {
        IndividualDropItemTable.Entry weighted = Group(1, 100, One, Entry(1015, 100, 100), Entry(1027, 100, 100));
        IndividualDropItemTable.Entry jobOnly = Group(2, 100, One, Entry(1015, 0, 1), Entry(1027, 0, 1));
        IndividualDropItemTable.Entry none = Group(3, 1, One, Entry(1015, 0), Entry(1027, 0));
        Assert.That(Tally(weighted, new Random(11)), Is.Empty);
        Assert.That(Tally(jobOnly, new Random(11)), Is.Empty);
        Assert.That(Tally(none, new Random(11)), Is.Empty);
    }

    // Key box and capsule: the same roll, of a box with a guaranteed group and a rare group; a capsule's rare group
    // share (KR capsule boxes roll one group of weighted entries, the rare ones at low weight).
    [Test]
    public void KeyBoxAndCapsule_RareEntryShare() {
        IndividualDropItemTable.Entry capsule = Group(1, 0, One,
            Entry(901, 5), Entry(902, 45), Entry(903, 450), Entry(904, 4500), Entry(905, 5000));
        Dictionary<int, int> tally = Tally(capsule, new Random(12));
        Assert.That(tally.Values.Sum(), Is.EqualTo(Rolls));
        Assert.That((double) tally.GetValueOrDefault(901) / Rolls, Is.EqualTo(0.0005).Within(0.001));
        Assert.That((double) tally[902] / Rolls, Is.EqualTo(0.0045).Within(0.002));
        AssertShare(tally[903], Rolls, 0.045);
        AssertShare(tally[904], Rolls, 0.45);
        AssertShare(tally[905], Rolls, 0.50);
    }

    [Test]
    public void Amount_EveryCountOfTheRangeAsOften() {
        IndividualDropItemTable.Item item = Entry(101, 1) with { DropCount = new IndividualDropItemTable.Range<int>(2, 5) };
        var random = new Random(13);
        int[] hits = new int[7];
        for (int i = 0; i < Rolls; i++) {
            hits[ItemDropManager.RollAmount(item, random)]++;
        }
        Assert.That(hits[0] + hits[1] + hits[6], Is.Zero);
        for (int amount = 2; amount <= 5; amount++) {
            AssertShare(hits[amount], Rolls, 0.25);
        }
    }

    // 9,506 of today's rows have no maxCount: they give their minCount. A maxCount below the minCount does the same
    // (it threw).
    [Test]
    public void Amount_WithoutMaxIsTheMin() {
        var random = new Random(14);
        Assert.That(ItemDropManager.RollAmount(Entry(101, 1) with { DropCount = new IndividualDropItemTable.Range<int>(1, 0) }, random), Is.EqualTo(1));
        Assert.That(ItemDropManager.RollAmount(Entry(101, 1) with { DropCount = new IndividualDropItemTable.Range<int>(5, 0) }, random), Is.EqualTo(5));
        Assert.That(ItemDropManager.RollAmount(Entry(101, 1) with { DropCount = new IndividualDropItemTable.Range<int>(5, 3) }, random), Is.EqualTo(5));
    }

    [Test]
    public void Rarity_SharesFollowTheProbabilities() {
        IndividualDropItemTable.Item item = Entry(101, 1) with {
            Rarities = [new IndividualDropItemTable.Item.Rarity(70, 1), new IndividualDropItemTable.Item.Rarity(25, 2), new IndividualDropItemTable.Item.Rarity(5, 4)],
        };
        var random = new Random(15);
        int[] hits = new int[7];
        for (int i = 0; i < Rolls; i++) {
            hits[ItemDropManager.RollRarity(item, random)]++;
        }
        AssertShare(hits[1], Rolls, 0.70);
        AssertShare(hits[2], Rolls, 0.25);
        AssertShare(hits[4], Rolls, 0.05);
        Assert.That(hits[3] + hits[5] + hits[6], Is.Zero);
    }

    [Test]
    public void Rarity_WithoutRowsIsTheItemsOwn() {
        Assert.That(ItemDropManager.RollRarity(Entry(101, 1), new Random(16)), Is.EqualTo(-1));
    }

    // Select box: no roll picks the item. The rows are the box's entries for the player's job and gender in table
    // order, whatever their weights and the group's count rows.
    [Test]
    public void SelectBox_RowsIgnoreWeightsAndCounts() {
        var limits = new Dictionary<int, ItemMetadataLimit> {
            [101] = Limit(Gender.All, JobCode.None),
            [102] = Limit(Gender.Female, JobCode.None),
            [103] = Limit(Gender.All, JobCode.Wizard),
            [104] = Limit(Gender.Male, JobCode.Knight),
        };
        IndividualDropItemTable.Entry[] box = [
            new(1, 0, [new(0, 100)], 99, false, true, [Entry(101, 0), Entry(102, 1), Entry(999, 5)]),
            new(2, 100, [], 0, false, true, [Entry(103, 0), Entry(104, 0)]),
        ];
        int[] Rows(JobCode job, Gender gender) => ItemDropManager.SelectBoxRows(box, id => limits.GetValueOrDefault(id), job, gender)
            .Select(item => item.Ids[0]).ToArray();

        Assert.That(Rows(JobCode.Knight, Gender.Male), Is.EqualTo(new[] { 101, 104 }));
        Assert.That(Rows(JobCode.Knight, Gender.Female), Is.EqualTo(new[] { 101, 102 }));
        Assert.That(Rows(JobCode.Wizard, Gender.Female), Is.EqualTo(new[] { 101, 102, 103 }));
        Assert.That(Rows(JobCode.Newbie, Gender.Male), Is.EqualTo(new[] { 101, 104 }));
    }

    private static ItemMetadataLimit Limit(Gender gender, JobCode recommend) =>
        new(gender, 0, 0, 4, true, true, true, true, true, false, false, 0, [], [recommend]);
}
