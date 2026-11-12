using Maple2.Model.Enum;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class QuestExpRulesTests {
    // Base exp times the factor, rounded, at least 1; a factor of 0 leaves 1 on every level.
    [TestCase(217, 10.0, 2170)]
    [TestCase(4644, 5.0, 23220)]
    [TestCase(3, 0.5, 2)]
    [TestCase(1, 0.4, 1)]
    [TestCase(4644, 0.0, 1)]
    public void CurveValue(long baseExp, double factor, long expected) {
        Assert.That(QuestExpRules.CurveValue(baseExp, factor), Is.EqualTo(expected));
    }

    // The larger of the fixed and the relative exp.
    [TestCase(0, 2170, 2170)]
    [TestCase(120, 37152, 37152)]
    [TestCase(3073, 1, 3073)]
    [TestCase(0, 0, 0)]
    public void Exp_TakesTheLarger(int fixedExp, long relative, long expected) {
        Assert.That(QuestExpRules.Exp(fixedExp, relative, QuestType.FieldMission, QuestExpRules.NoRank, 0), Is.EqualTo(expected));
    }

    // Field quests: 0.32 at any rank; alliance quests by rank, the first five alliances on their own table; a quest
    // without a rank, or of another type, keeps its exp. The rate is a float, as the client reads it, so 0.32 of
    // 10000 is 3199.
    [TestCase(QuestType.FieldQuest, 0, 3, 10000, 3199)]
    [TestCase(QuestType.FieldQuest, NoRankValue, 3, 10000, 10000)]
    [TestCase(QuestType.AllianceQuest, 0, 1, 10000, 50000)]
    [TestCase(QuestType.AllianceQuest, 3, 2, 10000, 4259)]
    [TestCase(QuestType.AllianceQuest, 4, 6, 10000, 10000)]
    [TestCase(QuestType.AllianceQuest, 0, 7, 10000, 13999)]
    [TestCase(QuestType.AllianceQuest, -2, 7, 10000, 13999)]
    [TestCase(QuestType.EpicQuest, 0, 1, 10000, 10000)]
    [TestCase(QuestType.GuildQuest, 2, 0, 10000, 10000)]
    public void Exp_RankFactor(QuestType type, int rank, int alliance, int fixedExp, long expected) {
        Assert.That(QuestExpRules.Exp(fixedExp, 0, type, rank, alliance), Is.EqualTo(expected));
    }

    // KMS2: exploration missions and epic quests of standard level 1 to 7 name 1053, the others 1010.
    [TestCase(QuestType.FieldMission, 0, ExpType.mission)]
    [TestCase(QuestType.FieldMission, 50, ExpType.mission)]
    [TestCase(QuestType.EpicQuest, 1, ExpType.mission)]
    [TestCase(QuestType.EpicQuest, 7, ExpType.mission)]
    [TestCase(QuestType.EpicQuest, 10, ExpType.quest)]
    [TestCase(QuestType.EpicQuest, 70, ExpType.quest)]
    [TestCase(QuestType.WorldQuest, 12, ExpType.quest)]
    [TestCase(QuestType.WorldQuest, 1, ExpType.quest)]
    [TestCase(QuestType.FieldQuest, 50, ExpType.quest)]
    [TestCase(QuestType.AllianceQuest, 50, ExpType.quest)]
    public void Reason(QuestType type, int standardLevel, ExpType expected) {
        Assert.That(QuestExpRules.Reason(type, standardLevel), Is.EqualTo(expected));
    }

    [Test]
    public void ReasonCodes() {
        Assert.That((int) Maple2.Model.ModelExtensions.Message(ExpType.mission), Is.EqualTo(1053));
        Assert.That((int) Maple2.Model.ModelExtensions.Message(ExpType.quest), Is.EqualTo(1010));
    }

    private const int NoRankValue = QuestExpRules.NoRank;
}
