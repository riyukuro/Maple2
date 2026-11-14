using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// WorldService.Disconnect (a login of the account, a character select, a GM) of a player on the way to another
// channel: the session on the target channel can be loaded and play while the record still names the channel it
// left (its login writes the new channel after the load). Before, only the record's channel was asked, which has
// no session any more, so the session on the target channel played on next to the login that ended the account.
public class DisconnectChannelChangeTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long CharacterId = 11;

    private sealed class FakeChannel : ChannelClient {
        public readonly List<long> Disconnects = [];
        public bool HasSession;

        public override DisconnectResponse Disconnect(DisconnectRequest request, CallOptions options) {
            Disconnects.Add(request.CharacterId);
            return new DisconnectResponse { Success = HasSession };
        }

        public override PlayerUpdateResponse UpdatePlayer(PlayerUpdateRequest request, CallOptions options) => new();
    }

    private FakeChannel left = null!;
    private FakeChannel target = null!;
    private ConcurrentDictionary<long, PlayerInfo> players = null!;
    private WorldServer worldServer = null!;
    private WorldService service = null!;

    private static void AddChannel(ChannelClientLookup lookup, int id, ChannelClient client) {
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), id, false, new IPEndPoint(IPAddress.Loopback, 0), client, null, (ushort) 0, 0], null)!;
        var channels = (System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(lookup)!;
        channels[id] = channel;
    }

    private static void Set(object target, string name, object value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    [SetUp]
    public void SetUp() {
        var channels = new ChannelClientLookup();
        left = new FakeChannel();
        target = new FakeChannel();
        AddChannel(channels, 1, left);
        AddChannel(channels, 2, target);

        // The lookup's cache only: no database and no sync loop.
        var lookup = (PlayerInfoLookup) RuntimeHelpers.GetUninitializedObject(typeof(PlayerInfoLookup));
        players = new ConcurrentDictionary<long, PlayerInfo>();
        Set(lookup, "cache", players);
        Set(lookup, "channels", channels);
        Set(lookup, "logger", new LoggerConfiguration().CreateLogger());
        players[CharacterId] = new PlayerInfo(new CharacterInfo(CharacterId, CharacterId, "player", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []) {
            Channel = 1,
        };

        worldServer = (WorldServer) RuntimeHelpers.GetUninitializedObject(typeof(WorldServer));
        GC.SuppressFinalize(worldServer);
        Set(worldServer, "<Migrations>k__BackingField", new Maple2.Server.Core.Helpers.MigrationTracker(WorldServer.MigrationLifetime));
        Set(worldServer, "playerInfoLookup", lookup);

        service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        Set(service, "channelClients", channels);
        Set(service, "playerLookup", lookup);
        Set(service, "worldServer", worldServer);
        Set(service, "logger", new LoggerConfiguration().CreateLogger());
    }

    private DisconnectResponse Disconnect() {
        return service.Disconnect(new DisconnectRequest { CharacterId = CharacterId, Force = true }, null!).Result;
    }

    [Test]
    public void ASessionThatArrivedOnTheTarget_IsEnded() {
        worldServer.Migrations.Begin(CharacterId, 1, 2, DateTime.UtcNow);
        target.HasSession = true;

        Assert.Multiple(() => {
            Assert.That(Disconnect().Success, Is.True);
            Assert.That(left.Disconnects, Is.EqualTo(new[] { CharacterId }));
            Assert.That(target.Disconnects, Is.EqualTo(new[] { CharacterId }));
            Assert.That(players[CharacterId].Channel, Is.EqualTo(-1));
        });
    }

    [Test]
    public void ASessionStillOnTheRecordsChannel_EndsThereOnly() {
        worldServer.Migrations.Begin(CharacterId, 1, 2, DateTime.UtcNow);
        left.HasSession = true;

        Disconnect();

        Assert.That(target.Disconnects, Is.Empty);
    }

    [Test]
    public void NoChannelChange_AsksTheRecordsChannelOnly() {
        Disconnect();

        Assert.Multiple(() => {
            Assert.That(left.Disconnects, Is.EqualTo(new[] { CharacterId }));
            Assert.That(target.Disconnects, Is.Empty);
            Assert.That(players[CharacterId].Channel, Is.EqualTo(-1));
        });
    }
}
