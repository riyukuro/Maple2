using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game.Packets;

public class MailNotifyRulesTests {
    private static PlayerInfo Info(short channel) =>
        new(new CharacterInfo(1, 2, "Receiver", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) { Channel = channel };

    [Test]
    public void OnlyAnOnlineReceiverIsNotified() {
        Assert.That(MailNotifyRules.Notify(null), Is.False);
        Assert.That(MailNotifyRules.Notify(Info(-1)), Is.False, "offline");
        Assert.That(MailNotifyRules.Notify(Info(0)), Is.True);
        Assert.That(MailNotifyRules.Notify(Info(2)), Is.True);
    }
}
