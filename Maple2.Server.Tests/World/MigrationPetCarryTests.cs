using System;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.World;
using Maple2.Server.World.Containers;
using Maple2.Server.World.Service;
using Microsoft.Extensions.Caching.Memory;
using NUnit.Framework;
using Serilog;
using ChannelClient = Maple2.Server.Channel.Service.Channel.ChannelClient;

namespace Maple2.Server.Tests.World;

// A move to a game server hands the target its summoned pet and the pet attack flag (MigrateOut's token, read back by
// the target's MigrateIn): the target summons the pet again and its login tells the client the player's own flag.
public class MigrationPetCarryTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Guid Machine = Guid.NewGuid();

    private sealed class FakeChannel : ChannelClient {
        public override PlayerUpdateResponse UpdatePlayer(PlayerUpdateRequest request, CallOptions options) => new();
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

    private static WorldService Service() {
        var channels = new ChannelClientLookup();
        Type channelType = typeof(ChannelClientLookup).GetNestedType("Channel", BindingFlags.NonPublic)!;
        object channel = Activator.CreateInstance(channelType, Any, null,
            [Enum.Parse(typeof(ChannelClientLookup).GetNestedType("ChannelStatus", BindingFlags.NonPublic)!, "Active"), 2, false, new IPEndPoint(IPAddress.Loopback, 0), new FakeChannel(), null, (ushort) 0, 0], null)!;
        ((System.Collections.IDictionary) typeof(ChannelClientLookup).GetField("channels", Any)!.GetValue(channels)!)[2] = channel;

        var lookup = (PlayerInfoLookup) RuntimeHelpers.GetUninitializedObject(typeof(PlayerInfoLookup));
        // The character's record, offline: no database behind the lookup.
        var players = new ConcurrentDictionary<long, PlayerInfo>();
        players[11] = new PlayerInfo(new CharacterInfo(1, 11, "player", "", "", Gender.Male, Job.Newbie, 1), "", new AchievementInfo(), []);
        Set(lookup, "cache", players);
        Set(lookup, "channels", channels);
        Set(lookup, "logger", new LoggerConfiguration().CreateLogger());

        var service = (WorldService) RuntimeHelpers.GetUninitializedObject(typeof(WorldService));
        Set(service, "channelClients", channels);
        Set(service, "playerLookup", lookup);
        Set(service, "tokenCache", new MemoryCache(new MemoryCacheOptions()));
        Set(service, "logger", new LoggerConfiguration().CreateLogger());
        return service;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GameMove_HandsTheTargetThePetAndItsAttackFlag(bool attackOff) {
        WorldService service = Service();
        ulong token = service.MigrateOut(new MigrateOutRequest {
            AccountId = 1,
            CharacterId = 11,
            MachineId = Machine.ToString(),
            Server = Maple2.Server.World.Service.Server.Game,
            Channel = 2,
            ExactChannel = true,
            PetUid = 77,
            PetAttackOff = attackOff,
        }, null!).Result.Token;

        MigrateInResponse entered = service.MigrateIn(new MigrateInRequest {
            AccountId = 1,
            Token = token,
            MachineId = Machine.ToString(),
        }, null!).Result;

        Assert.Multiple(() => {
            Assert.That(entered.Channel, Is.EqualTo(2));
            Assert.That(entered.PetUid, Is.EqualTo(77));
            Assert.That(entered.PetAttackOff, Is.EqualTo(attackOff));
        });
    }
}
