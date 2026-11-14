using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// The return_user name-tag symbol (KR 1 row) is shown for a registered mentee only (mentoring role 2), as the
// client's check reads the role the field's character block and Mentor 1 set.
public class InsigniaReturnUserTests {
    [TestCase(MentorRole.None, false)]
    [TestCase(MentorRole.MenteeCandidate, false)]
    [TestCase(MentorRole.Mentee, true)]
    [TestCase(MentorRole.UnregisteredMentee, false)]
    [TestCase(MentorRole.MentorCandidate, false)]
    [TestCase(MentorRole.Mentor, false)]
    [TestCase(MentorRole.UnregisteredMentor, false)]
    public void Shown_ForARegisteredMentee(MentorRole role, bool shown) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Character.MentorRole = role;

        var entry = new InsigniaTable.Entry(InsigniaConditionType.return_user, 0, 0, 0);
        Assert.That(InsigniaRules.Shown(session, entry), Is.EqualTo(shown));
    }
}
