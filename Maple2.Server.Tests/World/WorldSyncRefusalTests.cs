using System;
using System.Collections.Concurrent;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.World;

// The World service's player sync calls refuse what cannot be answered: no id is an invalid argument, an unknown
// character is not found (an unknown account answers empty), a forged gender or job is refused before it is stored,
// and a disconnect of an offline or unknown character is not found.
public class WorldSyncRefusalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long CharacterId = 1001;
    private TestGameSession test = null!;
    private PlayerInfoLookup playerLookup = null!;
    private WorldService world = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        var channels = new ChannelClientLookup();
        playerLookup = new PlayerInfoLookup(test.Storage, channels);
        var cache = (ConcurrentDictionary<long, PlayerInfo>) typeof(PlayerInfoLookup).GetField("cache", Any)!.GetValue(playerLookup)!;
        cache[CharacterId] = new PlayerInfo(new CharacterInfo(CharacterId + 100, CharacterId, "Sync", "", "", Gender.Male, Job.Newbie, 20), "",
            new AchievementInfo(), []) { Channel = 1 };
        world = new WorldService(null!, null!, channels, playerLookup, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!);
    }

    [TearDown]
    public void TearDown() {
        playerLookup.Dispose();
    }

    private static StatusCode Code(TestDelegate call) => Assert.Throws<RpcException>(call)!.StatusCode;

    [Test]
    public void PlayerInfo_AnswersTheKnownCharacterAndRefusesTheRest() {
        Assert.That(world.PlayerInfo(new PlayerInfoRequest { CharacterId = CharacterId }, null!).Result.Name, Is.EqualTo("Sync"));
        Assert.That(Code(() => world.PlayerInfo(new PlayerInfoRequest(), null!)), Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(Code(() => world.PlayerInfo(new PlayerInfoRequest { CharacterId = 5 }, null!)), Is.EqualTo(StatusCode.NotFound));
    }

    [Test]
    public void AccountInfo_AnswersTheKnownAccountAndEmptyForAnUnknownOne() {
        Assert.That(world.AccountInfo(new PlayerInfoRequest { AccountId = CharacterId + 100 }, null!).Result.CharacterId, Is.EqualTo(CharacterId));
        Assert.That(world.AccountInfo(new PlayerInfoRequest { AccountId = 5 }, null!).Result.CharacterId, Is.Zero);
        Assert.That(Code(() => world.AccountInfo(new PlayerInfoRequest(), null!)), Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void UpdatePlayer_RefusesAForgedGenderOrJob() {
        Assert.That(Code(() => world.UpdatePlayer(new PlayerUpdateRequest { CharacterId = CharacterId, Gender = 7 }, null!)),
            Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(Code(() => world.UpdatePlayer(new PlayerUpdateRequest { CharacterId = CharacterId, Job = 12345 }, null!)),
            Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void MailNotification_RefusesNoIdAndSkipsAnUnknownPlayer() {
        Assert.That(Code(() => world.MailNotification(new MailNotificationRequest(), null!)), Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(world.MailNotification(new MailNotificationRequest { CharacterId = 5, AccountId = 5 }, null!).Result, Is.Not.Null);
    }

    [Test]
    public void Disconnect_RefusesNoIdAndAnUnknownCharacter() {
        Assert.That(Code(() => world.Disconnect(new DisconnectRequest(), null!)), Is.EqualTo(StatusCode.InvalidArgument));
        Assert.That(Code(() => world.Disconnect(new DisconnectRequest { CharacterId = 5 }, null!)), Is.EqualTo(StatusCode.NotFound));
    }
}
