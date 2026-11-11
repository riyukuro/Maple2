using Maple2.Model.Game;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class GuildApplicationRulesTests {
    [TestCase(0, true)]
    [TestCase(9, true)]
    [TestCase(10, false)]
    [TestCase(11, false)]
    public void AtMostTenOpenRequests(int open, bool expected) {
        Assert.That(GuildRules.CanApply(open), Is.EqualTo(expected));
    }

    [Test]
    public void CancelIdIsTheGuildFirstThenTheRequest() {
        GuildApplicationRow[] own = [
            new GuildApplicationRow(Id: 7, GuildId: 3, ApplicantId: 1, CreationTime: 0),
            new GuildApplicationRow(Id: 3, GuildId: 9, ApplicantId: 1, CreationTime: 0),
        ];

        Assert.That(GuildRules.FindOwnApplication(own, 9)?.Id, Is.EqualTo(3));
        Assert.That(GuildRules.FindOwnApplication(own, 7)?.Id, Is.EqualTo(7));
        // 3 is both a guild and a request of this player: the guild wins, as the client names the id.
        Assert.That(GuildRules.FindOwnApplication(own, 3)?.Id, Is.EqualTo(7));
        Assert.That(GuildRules.FindOwnApplication(own, 5), Is.Null);
        Assert.That(GuildRules.FindOwnApplication([], 3), Is.Null);
    }
}
