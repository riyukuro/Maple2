using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager.Items;

namespace Maple2.Server.Tests.Game.Manager;

// The rows of a select box (SelectItemBox, parameters "select count,individual drop box id"), as the client's
// dialog lists them, on boxes shaped like KR boxes: 320000044 has 6 smart gender groups with a smart drop rate;
// groups 3 and 4 have one item for each of 11 jobs, the others one item for every job.
public class SelectBoxRowsTests {
    private static IndividualDropItemTable.Item Entry(int id, int id2 = 0) =>
        new([id, id2], false, 10000, 0, 10000, new IndividualDropItemTable.Range<int>(1, 1), [], 0, 0, false, false, false, false, [], false);

    private static IndividualDropItemTable.Entry Group(int groupId, bool smart, params IndividualDropItemTable.Item[] items) =>
        new(groupId, smart ? 100 : 0, [new(1, 100)], 0, false, smart, items.ToList());

    private static ItemMetadataLimit Limit(Gender gender, params JobCode[] recommends) =>
        new(gender, 0, 0, 4, true, true, true, true, true, false, false, 0, [], recommends);

    private static readonly JobCode[] Jobs = [JobCode.Knight, JobCode.Berserker, JobCode.Wizard, JobCode.Priest, JobCode.Archer,
        JobCode.HeavyGunner, JobCode.Thief, JobCode.Assassin, JobCode.RuneBlader, JobCode.Striker, JobCode.SoulBinder];

    private static readonly Dictionary<int, ItemMetadataLimit> Items = new() {
        [41000011] = Limit(Gender.All, JobCode.None),
        [41000021] = Limit(Gender.All, JobCode.None),
        [43000021] = Limit(Gender.All, JobCode.None),
        [43000031] = Limit(Gender.All, JobCode.None),
    };

    static SelectBoxRowsTests() {
        for (int i = 0; i < Jobs.Length; i++) {
            Items[42000011 + i * 10] = Limit(Gender.All, Jobs[i]);
            Items[42000121 + i * 10] = Limit(Gender.All, Jobs[i]);
        }
    }

    private static readonly IndividualDropItemTable.Entry[] Box = [
        Group(1, true, Entry(41000011)),
        Group(2, true, Entry(41000021)),
        Group(3, true, Enumerable.Range(0, 11).Select(i => Entry(42000011 + i * 10)).ToArray()),
        Group(4, true, Enumerable.Range(0, 11).Select(i => Entry(42000121 + i * 10)).ToArray()),
        Group(5, true, Entry(43000021)),
        Group(6, true, Entry(43000031)),
    ];

    private static int[] Rows(IEnumerable<IndividualDropItemTable.Entry> box, JobCode job, Gender gender = Gender.Male) =>
        ItemDropManager.SelectBoxRows(box, id => Items.GetValueOrDefault(id), job, gender).Select(item => item.Ids[0]).ToArray();

    [Test]
    public void EveryGroupIsListed_WithThePlayersJobItems() {
        // The client sends 3 for the fourth row, group 4's wizard item; the server read the first parameter (the
        // select count, 1) as a group id and counted group 1 only, so nothing was given and the box was used up.
        Assert.That(Rows(Box, JobCode.Wizard), Is.EqualTo(new[] { 41000011, 41000021, 42000031, 42000141, 43000021, 43000031 }));
        Assert.That(Rows(Box, JobCode.SoulBinder)[2..4], Is.EqualTo(new[] { 42000111, 42000221 }));
    }

    [Test]
    public void BeginnerCountsAsKnight() {
        Assert.That(Rows(Box, JobCode.Newbie), Is.EqualTo(Rows(Box, JobCode.Knight)));
    }

    [Test]
    public void BoxWithoutGroupOne_IsListed() {
        // Like KR box 320000095: one group, id 2.
        IndividualDropItemTable.Entry[] box = [Group(2, false, Entry(41000011))];
        Assert.That(Rows(box, JobCode.Wizard), Is.EqualTo(new[] { 41000011 }));
    }

    [Test]
    public void SmartGender_LeavesOutTheOtherGender() {
        Items[11300001] = Limit(Gender.Male);
        Items[11300002] = Limit(Gender.Female);
        Items[11300003] = Limit(Gender.All);
        IndividualDropItemTable.Entry[] smart = [Group(1, true, Entry(11300001), Entry(11300002), Entry(11300003))];
        IndividualDropItemTable.Entry[] plain = [Group(1, false, Entry(11300001), Entry(11300002), Entry(11300003))];
        Assert.Multiple(() => {
            Assert.That(Rows(smart, JobCode.Wizard, Gender.Female), Is.EqualTo(new[] { 11300002, 11300003 }));
            Assert.That(Rows(smart, JobCode.Wizard, Gender.Male), Is.EqualTo(new[] { 11300001, 11300003 }));
            Assert.That(Rows(plain, JobCode.Wizard, Gender.Female), Is.EqualTo(new[] { 11300001, 11300002, 11300003 }));
        });
    }

    [Test]
    public void JobFilter_OnlyWithASmartDropRate() {
        IndividualDropItemTable.Entry[] plain = [Group(3, false, Enumerable.Range(0, 11).Select(i => Entry(42000011 + i * 10)).ToArray())];
        Assert.That(Rows(plain, JobCode.Wizard), Has.Length.EqualTo(11));
    }

    // Picking the first row gives the first item only, the second row the second item only.
    [Test]
    public void ItemWithoutData_IsLeftOut_SecondItemIsARow() {
        IndividualDropItemTable.Entry[] box = [Group(1, false, Entry(49999999), Entry(41000011, 41000021), Entry(43000021))];
        Assert.That(Rows(box, JobCode.Wizard), Is.EqualTo(new[] { 41000011, 41000021, 43000021 }));
        Assert.That(ItemDropManager.SelectBoxRows(box, id => Items.GetValueOrDefault(id), JobCode.Wizard, Gender.Male).Take(2).Select(item => item.Ids), Is.EqualTo(new[] { new[] { 41000011 }, new[] { 41000021 } }));
    }

    [TestCase("3", 1, new[] { 3 })]
    [TestCase("0", 1, new[] { 0 })]
    [TestCase("2,5", 2, new[] { 2, 5 })]
    [TestCase("2,5", 1, null)]
    [TestCase("2", 2, null)]
    [TestCase("2,2", 2, null)]
    [TestCase("", 1, null)]
    [TestCase("-1", 1, null)]
    [TestCase(" 1", 1, null)]
    [TestCase("1", 0, null)]
    public void Selection(string text, int selectCount, int[]? expected) {
        Assert.That(ItemBoxRules.ParseSelection(text, selectCount), Is.EqualTo(expected));
    }
}
