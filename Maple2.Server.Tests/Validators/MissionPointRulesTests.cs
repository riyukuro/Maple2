using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class MissionPointRulesTests {
    // The daily missions give 30 points each; the rows ask for 100 to 900.
    [TestCase(30, 30)]
    [TestCase(0, 0)] // a quest without mission points
    [TestCase(-5, 0)]
    public void CompletedQuest_CountsItsRewardPoints(int rewardMissionPoint, int expected) {
        Assert.That(MissionPointRules.PointsCounted(rewardMissionPoint), Is.EqualTo(expected));
    }

    // Four missions of 30 points reach the 100 point row, three do not: the quest counter adds each count.
    [TestCase(3, false)]
    [TestCase(4, true)]
    public void FourDailyMissions_ReachTheFirstRow(int missions, bool reached) {
        int counter = 0;
        for (int i = 0; i < missions; i++) {
            counter += MissionPointRules.PointsCounted(30);
        }
        Assert.That(counter >= 100, Is.EqualTo(reached));
    }
}
