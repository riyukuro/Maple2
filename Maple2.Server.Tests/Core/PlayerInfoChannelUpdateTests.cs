using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Core.Sync;

namespace Maple2.Server.Tests.Core;

// The heartbeat's offline update against a login on another channel (WorldServer.Heartbeat, PlayerInfoLookup).
public class PlayerInfoChannelUpdateTests {
    private static PlayerInfo OnChannel(int channel) {
        return new PlayerInfo(new CharacterInfo(1, 2, "a", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) {
            Channel = (short) channel,
        };
    }

    private static PlayerUpdateRequest Offline() {
        return new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, Channel = -1, LastOnlineTime = 100 };
    }

    private static void Login(PlayerInfo info, int channel) {
        var request = new PlayerUpdateRequest { AccountId = 1, CharacterId = 2, Channel = channel };
        info.Apply(new PlayerInfoUpdateEvent(info, request));
    }

    [Test]
    public void OfflineIsAppliedAtOnce() {
        PlayerInfo info = OnChannel(1);

        PlayerInfoUpdateEvent? applied = info.ApplyIfOn(1, Offline());

        Assert.That(applied, Is.Not.Null);
        Assert.That(applied!.Type.HasFlag(UpdateField.Channel), Is.True);
        Assert.That(info.Channel, Is.EqualTo(-1));
        Assert.That(info.Online, Is.False);
        Assert.That(info.LastOnlineTime, Is.EqualTo(100));
    }

    // Miss on channel 1, then the login on channel 2: the player is online on 2.
    [Test]
    public void OfflineThenLogin_EndsOnline() {
        PlayerInfo info = OnChannel(1);

        info.ApplyIfOn(1, Offline());
        Login(info, 2);

        Assert.That(info.Channel, Is.EqualTo(2));
        Assert.That(info.Online, Is.True);
    }

    // Login on channel 2, then the late miss of channel 1: nothing is written and nothing is to be sent.
    [Test]
    public void LoginThenLateOffline_IsDropped() {
        PlayerInfo info = OnChannel(1);

        Login(info, 2);
        PlayerInfoUpdateEvent? applied = info.ApplyIfOn(1, Offline());

        Assert.That(applied, Is.Null);
        Assert.That(info.Channel, Is.EqualTo(2));
        Assert.That(info.LastOnlineTime, Is.Not.EqualTo(100));
    }

    [Test]
    public void AlreadyOffline_IsDropped() {
        PlayerInfo info = OnChannel(-1);

        Assert.That(info.ApplyIfOn(1, Offline()), Is.Null);
    }
}
