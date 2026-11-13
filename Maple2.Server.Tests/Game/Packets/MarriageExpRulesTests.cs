using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game.Packets;

// Quest weddingExp in the marriage's exp log: the client's two mission lines, who counts it, and the per-view order.
public class MarriageExpRulesTests {
    private const long First = 11;
    private const long Second = 22;

    [Test]
    public void MissionTypes_HaveTheClientsNumbers() {
        Assert.That((short) MarriageExpType.WeddingMissionGroom, Is.EqualTo(11));
        Assert.That((short) MarriageExpType.WeddingMissionBride, Is.EqualTo(12));
    }

    [Test]
    public void WeddingMission_NamesThePartnerByTheStoredOrder() {
        Assert.That(MarriageExpRules.WeddingMission(First, First), Is.EqualTo(MarriageExpType.WeddingMissionGroom));
        Assert.That(MarriageExpRules.WeddingMission(Second, First), Is.EqualTo(MarriageExpType.WeddingMissionBride));
    }

    [TestCase(MaritalStatus.Single, false)]
    [TestCase(MaritalStatus.Engaged, false)]
    [TestCase(MaritalStatus.Married, true)]
    [TestCase(MaritalStatus.DivorceFiledByGroom, true)]
    [TestCase(MaritalStatus.DivorceFiledByBride, true)]
    [TestCase(MaritalStatus.Divorced, false)]
    public void CountsWeddingExp(MaritalStatus status, bool counts) {
        Assert.That(MarriageExpRules.CountsWeddingExp(status), Is.EqualTo(counts));
    }

    [TestCase(MarriageExpType.WeddingMissionGroom, MarriageExpType.WeddingMissionBride)]
    [TestCase(MarriageExpType.WeddingMissionBride, MarriageExpType.WeddingMissionGroom)]
    [TestCase(MarriageExpType.coupleMessage, MarriageExpType.coupleMessage)]
    public void Swapped(MarriageExpType type, MarriageExpType swapped) {
        Assert.That(MarriageExpRules.Swapped(type), Is.EqualTo(swapped));
    }
}
