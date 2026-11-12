using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class MasteryRulesTests {
    // KMS2: the exp of a gather or a craft names 3100 plus the mastery type.
    [TestCase(MasteryType.Mining, 3103)]
    [TestCase(MasteryType.Gathering, 3104)]
    [TestCase(MasteryType.Breeding, 3105)]
    [TestCase(MasteryType.Farming, 3106)]
    [TestCase(MasteryType.Blacksmithing, 3107)]
    [TestCase(MasteryType.Engraving, 3108)]
    [TestCase(MasteryType.Alchemist, 3109)]
    [TestCase(MasteryType.Cooking, 3110)]
    public void ExpReason(MasteryType type, int expected) {
        Assert.That((int?) MasteryRules.ExpReason(type), Is.EqualTo(expected));
    }

    [TestCase(MasteryType.Unknown)]
    [TestCase(MasteryType.Fishing)]
    [TestCase(MasteryType.Music)]
    [TestCase(MasteryType.PetTaming)]
    public void ExpReasonOtherTypes(MasteryType type) {
        Assert.That(MasteryRules.ExpReason(type), Is.Null);
    }

    [Test]
    public void ExpReasonNames() {
        Assert.That(MasteryRules.ExpReason(MasteryType.Mining), Is.EqualTo(ExpMessageCode.gathering));
        Assert.That(MasteryRules.ExpReason(MasteryType.Blacksmithing), Is.EqualTo(ExpMessageCode.manufacturing));
        Assert.That(MasteryRules.ExpReason(MasteryType.Farming), Is.EqualTo(ExpMessageCode.masteryFarming));
    }

    // Mastery 0x0092 mode 2: u8 mode, i32 recipe id.
    [Test]
    public void CraftPacket() {
        var packet = new ByteReader(new byte[] { 0x02, 0x40, 0x42, 0x0F, 0x00 }, 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadInt(), Is.EqualTo(1000000));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(10, 20, 0, false, 100L, 0L, MasteryError.s_mastery_error_lack_mastery)]
    [TestCase(20, 20, 0, false, 100L, 0L, (MasteryError) 0)]
    [TestCase(0, 0, 2, false, 100L, 0L, MasteryError.s_mastery_error_lack_quest)]
    [TestCase(0, 0, 2, true, 100L, 0L, (MasteryError) 0)] // one of the two quests is enough
    [TestCase(0, 0, 0, false, 99L, 100L, MasteryError.s_mastery_error_lack_meso)]
    [TestCase(0, 0, 0, false, 100L, 100L, (MasteryError) 0)]
    [TestCase(10, 20, 2, false, 0L, 100L, MasteryError.s_mastery_error_lack_mastery)] // mastery is checked first
    [TestCase(20, 20, 2, false, 0L, 100L, MasteryError.s_mastery_error_lack_quest)]
    public void CraftChecks(int mastery, int requiredMastery, int questCount, bool anyQuest, long meso, long requiredMeso, MasteryError expected) {
        Assert.That(MasteryRules.CraftError(mastery, requiredMastery, questCount, anyQuest, meso, requiredMeso), Is.EqualTo(expected));
    }

    // Grade rows of a production type (grade, value) and the difference factors, as the tables give them.
    private static readonly (int Grade, int Value)[] Grades = [(1, 0), (2, 10), (3, 50), (4, 230), (5, 550), (6, 1300)];

    private static int? FactorOf(int difference) {
        return difference switch {
            0 => 10000,
            1 => 6000,
            2 => 2000,
            >= 3 and <= 14 => 0,
            _ => null,
        };
    }

    [TestCase(0, 1)]
    [TestCase(9, 1)]
    [TestCase(10, 2)]
    [TestCase(49, 2)]
    [TestCase(50, 3)]
    [TestCase(229, 3)]
    [TestCase(1299, 5)]
    [TestCase(1300, 6)]
    [TestCase(99999, 6)] // above the last row: the last grade
    public void GradeEdges(int mastery, int expected) {
        Assert.That(MasteryRules.Grade(Grades, mastery), Is.EqualTo(expected));
    }

    [Test]
    public void GradeWithoutRowsIsZero() {
        Assert.That(MasteryRules.Grade([], 100), Is.EqualTo(0));
    }

    // A recipe that needs mastery 0 (grade 1) and gives 5: whole in grade 1, 60 % in grade 2, 20 % in
    // grade 3, nothing from grade 4.
    [TestCase(5, 0, 0, 5)]
    [TestCase(5, 0, 9, 5)]
    [TestCase(5, 0, 10, 3)]
    [TestCase(5, 0, 49, 3)]
    [TestCase(5, 0, 50, 1)]
    [TestCase(5, 0, 229, 1)]
    [TestCase(5, 0, 230, 0)]
    // The starter recipes give 1: 0.6 rounds up to 1, 0.2 down to 0.
    [TestCase(1, 0, 9, 1)]
    [TestCase(1, 0, 10, 1)]
    [TestCase(1, 0, 50, 0)]
    // A recipe that needs mastery 10 (grade 2) and gives 2.
    [TestCase(2, 10, 9, 0)] // below the recipe's grade: a negative difference gives nothing
    [TestCase(2, 10, 10, 2)]
    [TestCase(2, 10, 49, 2)]
    [TestCase(2, 10, 50, 1)] // 1.2
    [TestCase(2, 10, 230, 0)] // 0.4
    [TestCase(2, 10, 550, 0)]
    public void ProductionRewardByGradeDifference(int rewardMastery, int requireMastery, int mastery, int expected) {
        Assert.That(MasteryRules.Reward(MasteryType.Mining, rewardMastery, requireMastery, mastery, Grades, FactorOf), Is.EqualTo(expected));
        Assert.That(MasteryRules.Reward(MasteryType.Farming, rewardMastery, requireMastery, mastery, Grades, FactorOf), Is.EqualTo(expected));
    }

    [Test]
    public void RewardRoundsHalfUp() {
        Assert.That(MasteryRules.Reward(MasteryType.Gathering, 5, 0, 0, Grades, _ => 1000), Is.EqualTo(1)); // 0.5
        Assert.That(MasteryRules.Reward(MasteryType.Gathering, 4, 0, 0, Grades, _ => 1000), Is.EqualTo(0)); // 0.4
    }

    [Test]
    public void DifferenceWithoutRowGivesNothing() {
        Assert.That(MasteryRules.Reward(MasteryType.Breeding, 5, 0, 0, Grades, _ => null), Is.EqualTo(0));
    }

    [TestCase(MasteryType.Fishing)]
    [TestCase(MasteryType.Music)]
    [TestCase(MasteryType.Cooking)]
    public void OtherTypesGetTheWholeReward(MasteryType type) {
        Assert.That(MasteryRules.Reward(type, 5, 0, 99999, Grades, _ => 0), Is.EqualTo(5));
    }

    // The client's id of a grade: type * 1000 + grade.
    [TestCase(3002, 3, 2)]
    [TestCase(3102, 3, 102)]
    [TestCase(11010, 11, 10)]
    [TestCase(2, 0, 2)]
    [TestCase(-1, -1, -1)]
    public void RewardStep_SplitsTheClientId(int id, int type, int grade) {
        Assert.That(MasteryRules.RewardStep(id), Is.EqualTo((type, grade)));
    }
}
