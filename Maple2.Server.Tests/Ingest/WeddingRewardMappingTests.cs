using Maple2.File.Ingest.Helpers;
using Maple2.File.Parser.Enum;
using Maple2.Model.Enum;

namespace Maple2.Server.Tests.Ingest;

// The wedding reward table's rows are keyed by the server type of the same name. Before, the parser's number was
// cast, so the Blessing Charm's weddingExpItem row (5) sat under no server type and month (3) was no limit.
public class WeddingRewardMappingTests {
    [TestCase(WeddingRewardType.weddingExpItem, MarriageExpType.weddingExpItem)]
    [TestCase(WeddingRewardType.coupleMessage, MarriageExpType.coupleMessage)]
    [TestCase(WeddingRewardType.attendance, MarriageExpType.attendance)]
    [TestCase(WeddingRewardType.logoutPanelty, MarriageExpType.logoutPanelty)]
    [TestCase(WeddingRewardType.marriedLife, MarriageExpType.marriedLife)]
    [TestCase(WeddingRewardType.marriageRegister, MarriageExpType.marriageRegister)]
    public void Type_ByName(WeddingRewardType type, MarriageExpType expected) {
        Assert.That(WeddingRewardMapping.Type(type), Is.EqualTo(expected));
    }

    [Test]
    public void Type_None_IsLeftOut() {
        Assert.That(WeddingRewardMapping.Type(WeddingRewardType.none), Is.Null);
    }

    [TestCase(WeddingRewardLimit.none, MarriageExpLimit.none)]
    [TestCase(WeddingRewardLimit.day, MarriageExpLimit.day)]
    [TestCase(WeddingRewardLimit.week, MarriageExpLimit.week)]
    [TestCase(WeddingRewardLimit.month, MarriageExpLimit.month)]
    public void Limit_ByName(WeddingRewardLimit limit, MarriageExpLimit expected) {
        Assert.That(WeddingRewardMapping.Limit(limit), Is.EqualTo(expected));
    }
}
