using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class SurvivalItemRulesTests {
    private static readonly MapMetadataSurvival Match = new(UserDrop: 1, UserAdditionalDrop: 311000039, PlusInven: 1,
        PlusInvenBasic: 311000042, PlusInvenZero: 1);

    [Test]
    public void MatchOnlyNeedsTheFlag() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.IsMatchOnly(20000897, true), Is.True);
            Assert.That(SurvivalItemRules.IsMatchOnly(40400083, true), Is.True);
            Assert.That(SurvivalItemRules.IsMatchOnly(20000897, false), Is.False);
        });
    }

    [Test]
    public void OutsideSourcesStayNormalItems() {
        foreach (int id in new[] { 30001099, 30001102, 30001103, 30001104, 40400067, 50600212 }) {
            Assert.That(SurvivalItemRules.IsMatchOnly(id, true), Is.False, $"{id}");
        }
        Assert.That(SurvivalItemRules.OutsideSourceIds, Has.Count.EqualTo(6));
    }

    [Test]
    public void ClearReasonByField() {
        Assert.Multiple(() => {
            Assert.That(SurvivalItemRules.ClearReason(null), Is.EqualTo(SurvivalItemRules.LeftMatch));
            Assert.That(SurvivalItemRules.ClearReason(Match), Is.EqualTo(SurvivalItemRules.MatchStart));
            Assert.That(SurvivalItemRules.ClearReason(Match with { PlusInvenZero = 0 }), Is.Null);
        });
    }
}
