using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Core;

// A mentoring registration (and its season-end reset) sends the channel's new role alone. The World record and every
// channel's copy take it: another player's mentee check reads the role from that record.
public class PlayerInfoMentorRoleTests {
    private static PlayerInfo Held(MentorRole role) {
        return new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1) { MentorRole = role }, "", new AchievementInfo(), []);
    }

    [TestCase(MentorRole.None, MentorRole.Mentee)]
    [TestCase(MentorRole.None, MentorRole.Mentor)]
    [TestCase(MentorRole.Mentee, MentorRole.None)]
    public void RoleOnly_IsApplied(MentorRole held, MentorRole sent) {
        PlayerInfo info = Held(held);

        PlayerInfoUpdateEvent? update = info.ApplyChanged(new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, MentorRole = (int) sent });

        Assert.Multiple(() => {
            Assert.That(update?.Type, Is.EqualTo(UpdateField.Profile));
            Assert.That(info.MentorRole, Is.EqualTo(sent));
        });
    }

    [Test]
    public void SameRole_NoChange() {
        PlayerInfo info = Held(MentorRole.Mentee);

        Assert.That(info.ApplyChanged(new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, MentorRole = (int) MentorRole.Mentee }), Is.Null);
    }
}
