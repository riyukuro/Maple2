using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class TrophyPointTests {
    // The trophy_point rows count a new trophy grade when the trophy's id is in their range (20000000 to 29999999),
    // which is the id of the trophy that reached the grade.
    [TestCase(23100112, true)]
    [TestCase(20000000, true)]
    [TestCase(29999999, true)]
    [TestCase(31000001, false)]
    [TestCase(11100001, false)]
    public void Row_CountsATrophyOfItsRange(int achievementId, bool expected) {
        var condition = new ConditionMetadata(ConditionType.trophy_point, 10,
            new ConditionMetadata.Parameters(Range: new ConditionMetadata.Range<int>(20000000, 29999999)), null);
        Assert.That(condition.Check(null!, codeLong: achievementId), Is.EqualTo(expected));
    }
}
