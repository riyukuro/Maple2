using System;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The daily reset clears the day's claims made before midnight; one made since is the new day's and stays. A purchase
// store takes a reset still due (BeginWithResets) after the claim was set in memory (a dungeon clear's sub count, the
// mini-game extra reward count, the interior check-in), and the World's reset can run after a claim stored past
// midnight: the reset stored 0 with the claim paid, and the new day's claim could be taken once more.
public class DailyResetKeepsTheNewDayTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;

    [TearDown]
    public void TearDown() => session?.Dispose();

    [TestCase(true)]
    [TestCase(false)]
    public void TheReset_KeepsAClaimMadeSinceMidnight(bool sinceMidnight) {
        test = new TestGameSession();
        test.Start();
        long dayStart = PetFusionRules.LastResetTime();
        long claimed = sinceMidnight ? dayStart + 1 : dayStart - 1;
        var record = new DungeonRecord(29000001) {
            UnionSubClears = 1,
            // A clear sets the next midnight from its own time.
            UnionSubCooldownTimestamp = sinceMidnight ? dayStart + 86400 : dayStart,
        };
        session.Dungeon.Records[record.DungeonId] = record;
        session.Player.Value.Character.MassiveExtraRewardCount = 2;
        session.Player.Value.Character.MassiveExtraRewardTime = claimed;
        session.Player.Value.Home.DecorationRewardTimestamp = claimed;

        session.DailyReset();

        Assert.Multiple(() => {
            Assert.That(record.UnionSubClears, Is.EqualTo(sinceMidnight ? 1 : 0));
            Assert.That(session.Player.Value.Character.MassiveExtraRewardCount, Is.EqualTo(sinceMidnight ? 2 : 0));
            Assert.That(session.Player.Value.Home.DecorationRewardTimestamp, Is.EqualTo(sinceMidnight ? claimed : 0));
        });
    }
}
