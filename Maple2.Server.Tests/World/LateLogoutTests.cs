using System;
using System.Collections.Concurrent;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.World;

// A channel's logout comes after the session's final save, so the World may have set the player offline first and
// the account may have logged in on another channel since: the late logout must not mark that login offline.
public class LateLogoutTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long CharacterId = 7001;

    private PlayerInfoLookup lookup = null!;
    private PlayerInfo info = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var storage = new GameStorage(options, null!, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
        lookup = new PlayerInfoLookup(storage, new ChannelClientLookup());
        info = new PlayerInfo(new CharacterInfo(CharacterId + 100, CharacterId, "Player", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(lookup)!;
        cache[CharacterId] = info;
    }

    [TearDown]
    public void TearDown() {
        lookup.Dispose();
    }

    private static PlayerUpdateRequest Logout(int fromChannel) => new() {
        AccountId = CharacterId + 100,
        CharacterId = CharacterId,
        LastOnlineTime = 1234,
        MapId = 0,
        Channel = -1,
        FromChannel = fromChannel,
    };

    [Test]
    public void TheLogoutOfTheRecordsChannel_SetsItOffline() {
        info.Channel = 1;
        Assert.That(lookup.ApplyLogout(Logout(1)), Is.True);
        Assert.That(info.Channel, Is.EqualTo(-1));
        Assert.That(info.LastOnlineTime, Is.EqualTo(1234));
    }

    [Test]
    public void ALateLogout_KeepsALoginOnAnotherChannel() {
        info.Channel = 2;
        Assert.That(lookup.ApplyLogout(Logout(1)), Is.False);
        Assert.That(info.Channel, Is.EqualTo(2));
        Assert.That(info.LastOnlineTime, Is.Not.EqualTo(1234));
    }

    // The World set the player offline first (its heartbeat, or a kick that waited too long): the logout still
    // applies (its time, its cleanups).
    [Test]
    public void ALogoutOfARecordAlreadyOffline_Applies() {
        info.Channel = -1;
        Assert.That(lookup.ApplyLogout(Logout(1)), Is.True);
        Assert.That(info.Channel, Is.EqualTo(-1));
        Assert.That(info.LastOnlineTime, Is.EqualTo(1234));
        // Nothing left to change: still a logout to clean up after.
        Assert.That(lookup.ApplyLogout(Logout(1)), Is.True);
    }
}
