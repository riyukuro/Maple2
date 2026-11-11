using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Game.Packets;

// Buddy and marriage requests to an offline receiver (channel -1) are not forwarded, as mail notices are not.
public class WorldForwardRulesTests {
    private static PlayerInfo Info(short channel) =>
        new(new CharacterInfo(1, 2, "Receiver", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) { Channel = channel };

    [Test]
    public void OnlyAnOnlineReceiverIsReached() {
        Assert.That(WorldForwardRules.Forward(null), Is.False);
        Assert.That(WorldForwardRules.Forward(Info(-1)), Is.False, "offline");
        Assert.That(WorldForwardRules.Forward(Info(0)), Is.True);
        Assert.That(WorldForwardRules.Forward(Info(3)), Is.True);
        Assert.That(MailNotifyRules.Notify(Info(-1)), Is.EqualTo(WorldForwardRules.Forward(Info(-1))));
    }
}
