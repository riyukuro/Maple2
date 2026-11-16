using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A trophy grade whose reward is an emote (dynamicaction): the emote is taught when the grade is reached (the packet
// and the trophy update are sent), and a claim after that has nothing due. Made-up trophy and emote ids.
public class TrophyEmoteRewardTests {
    private const int Trophy = 90000031, Emote = 11000123;

    [Test]
    public void Reward_TeachesTheEmoteWhenTheGradeIsReached() {
        var test = new TestGameSession();
        AchievementMetadata trophy = TestGameSession.Trophy(Trophy, ConditionType.use_merat, [10, 1000]);
        var grades = new Dictionary<int, AchievementMetadataGrade>(trophy.Grades);
        grades[1] = grades[1] with { Reward = new AchievementMetadataReward(AchievementRewardType.dynamicaction, Emote, 1, 1) };
        test.AddAchievement(trophy with { Grades = grades });
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(90000100));
        session.Currency.Meret = 100;
        test.TakeSent();

        // Only an item grade waits for the claim: the emote is taught as the grade is reached.
        session.Currency.Meret -= 10;
        List<SendOp> reached = test.TakeSentOps();
        session.Achievement.ClaimReward(Trophy);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Unlock.Emotes, Does.Contain(Emote));
            Assert.That(reached, Does.Contain(SendOp.Emote));
            Assert.That(reached.Last(), Is.EqualTo(SendOp.Achieve));
            Assert.That(test.TakeSentOps(), Is.Empty);
        });
    }
}
