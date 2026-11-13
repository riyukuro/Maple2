using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class FieldWarRulesTests {
    // The KR table: groups 1 and 2 hold two wars each, every one of weight 1.
    private static readonly List<FieldWarMetadata> Wars = [
        new(10000005, 1, 1, 2020065, 31000003, 2020013),
        new(10000001, 1, 1, 2020061, 31000003, 2020003),
        new(10000002, 2, 1, 2020062, 31000003, 2020032),
        new(10000003, 2, 1, 2020063, 31000003, 2020009),
    ];

    [TestCase(1, 0, ExpectedResult = 10000001)]
    [TestCase(1, 1, ExpectedResult = 10000005)]
    [TestCase(2, 0, ExpectedResult = 10000002)]
    [TestCase(2, 1, ExpectedResult = 10000003)]
    [TestCase(2, 2, ExpectedResult = null)]
    [TestCase(3, 0, ExpectedResult = null)]
    public int? Pick_ByWeightInIdOrder(int groupId, int roll) => FieldWarRules.Pick(Wars, groupId, roll)?.Id;

    [Test]
    public void Pick_WeightsAndZeroWeight() {
        List<FieldWarMetadata> wars = [new(1, 1, 3, 100, 0, 0), new(2, 1, 0, 200, 0, 0), new(3, 1, 1, 300, 0, 0)];
        Assert.Multiple(() => {
            Assert.That(FieldWarRules.TotalWeight(wars, 1), Is.EqualTo(4));
            Assert.That(FieldWarRules.Pick(wars, 1, 2)?.Id, Is.EqualTo(1));
            Assert.That(FieldWarRules.Pick(wars, 1, 3)?.Id, Is.EqualTo(3));
            Assert.That(FieldWarRules.TotalWeight(wars, 2), Is.EqualTo(0));
        });
    }

    // FieldWarEnd isClear 1 gives the war of the map's rewardID; a failed war or a map of no war, nothing.
    [TestCase(2020061, true, ExpectedResult = 31000003)]
    [TestCase(2020065, true, ExpectedResult = 31000003)]
    [TestCase(2020061, false, ExpectedResult = 0)]
    [TestCase(2020069, true, ExpectedResult = 0)]
    public int ClearReward_ByMap(int mapId, bool isClear) => FieldWarRules.ClearReward(Wars, mapId, isClear);

    [Test]
    public void ClearReward_LowestIdOfSharedMap() {
        List<FieldWarMetadata> wars = [new(7, 1, 1, 100, 70, 0), new(3, 2, 1, 100, 30, 0), new(5, 1, 1, 200, 50, 0)];
        Assert.That(FieldWarRules.ClearReward(wars, 100, true), Is.EqualTo(30));
    }

    [TestCase(70, 70, 22200490, true, ExpectedResult = true)]
    [TestCase(69, 70, 22200490, true, ExpectedResult = false)]
    [TestCase(80, 70, 22200490, false, ExpectedResult = false)]
    [TestCase(1, 0, 0, false, ExpectedResult = true)]
    public bool Eligible(int level, int requireLevel, int achievementId, bool held) =>
        FieldWarRules.Eligible(level, requireLevel, achievementId, id => held && id == achievementId);

    [TestCase(4, 4, ExpectedResult = FieldWarRules.Start)]
    [TestCase(10, 4, ExpectedResult = FieldWarRules.Start)]
    [TestCase(3, 4, ExpectedResult = FieldWarRules.Close)]
    [TestCase(0, 4, ExpectedResult = FieldWarRules.Close)]
    public int StartValue(int players, int requirePlayers) => FieldWarRules.StartValue(players, requirePlayers);

    [TestCase(0, 0L, 0L, 0, 10, ExpectedResult = true)]          // no room yet
    [TestCase(5, 1000L, 60_999L, 3, 10, ExpectedResult = false)] // in its window
    [TestCase(5, 1000L, 61_000L, 3, 10, ExpectedResult = true)]  // the window is over
    [TestCase(5, 1000L, 2000L, 10, 10, ExpectedResult = true)]   // full
    [TestCase(5, 1000L, 2000L, 50, 0, ExpectedResult = false)]   // no maxCount
    public bool NeedsNewRoom(int roomId, long opened, long now, int joined, int maxCount) =>
        FieldWarRules.NeedsNewRoom(roomId, opened, now, 60, joined, maxCount);
}
