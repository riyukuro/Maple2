using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The party buff level (members of the party in the same field) and the exp/meso rate of a kill.
public class PartyRateTests {
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(9, 3)]
    public void LevelByOthersHere(int others, int level) {
        Assert.That(PartyBuff.Level(others), Is.EqualTo(level));
    }

    // The returning-user party buff: a registered returning character (role 2) among the party members here, the
    // player or another, with at least one other member here; a candidate (1) or an unregistered character (3) is not one.
    [TestCase(MentorRole.Mentee, new MentorRole[] { MentorRole.Mentor }, true)]
    [TestCase(MentorRole.Mentee, new MentorRole[] { MentorRole.None }, true)]
    [TestCase(MentorRole.None, new MentorRole[] { MentorRole.None, MentorRole.Mentee }, true)]
    [TestCase(MentorRole.Mentor, new MentorRole[] { MentorRole.Mentee }, true)]
    [TestCase(MentorRole.Mentee, new MentorRole[0], false)]
    [TestCase(MentorRole.None, new MentorRole[] { MentorRole.Mentor, MentorRole.None }, false)]
    [TestCase(MentorRole.MenteeCandidate, new MentorRole[] { MentorRole.UnregisteredMentee }, false)]
    public void ReturnUserBuffByRolesHere(MentorRole self, MentorRole[] others, bool given) {
        Assert.That(PartyBuff.ReturnUser(self, others), Is.EqualTo(given));
    }

    [Test]
    public void RateRaisesAmount() {
        Assert.Multiple(() => {
            Assert.That(KillRate.Apply(1000L, 0.05), Is.EqualTo(1050L));
            Assert.That(KillRate.Apply(200, 0.3), Is.EqualTo(260));
            Assert.That(KillRate.Apply(1000L, 0d), Is.EqualTo(1000L));
        });
    }

    [Test]
    public void RateNeverNegative() {
        Assert.That(KillRate.Apply(1000L, -2d), Is.EqualTo(0L));
    }

    [Test]
    public void IntRateCapped() {
        Assert.That(KillRate.Apply(int.MaxValue, 1d), Is.EqualTo(int.MaxValue));
    }
}
