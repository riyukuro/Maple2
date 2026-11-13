using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Trigger;
using Maple2.Server.World.Service;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Game;

// give_guild_exp (the massive event maps when the game starts): each guild with a member in the event gains its
// level's mini game exp and fund once, through the World.
public class GuildGainExpTests {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class GuildWorld : WorldClient {
        public readonly List<GuildRequest> Requests = [];

        public override GuildResponse Guild(GuildRequest request, CallOptions options) {
            Requests.Add(request);
            return new GuildResponse();
        }
    }

    private static T Bare<T>() {
        var bare = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(bare!);
        return bare;
    }

    private static void Set<T>(object target, string field, object? value) {
        (typeof(T).GetField(field, Flags) ?? throw new MissingFieldException(typeof(T).Name, field)).SetValue(target, value);
    }

    private static TableMetadataStorage Tables() {
        GuildTable.Property Row(short level, int accumExp, int loseExp, int loseFund) =>
            ((GuildTable.Property) RuntimeHelpers.GetUninitializedObject(typeof(GuildTable.Property))) with {
                Level = level,
                Experience = accumExp,
                LoseMiniGameExp = loseExp,
                LoseMiniGameFund = loseFund,
                WinMiniGameExp = loseExp * 2,
            };
        var table = new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property> {
                [1] = Row(1, 10000, 2000, 400000),
                [2] = Row(2, 30000, 2500, 500000),
            });
        var tables = Bare<TableMetadataStorage>();
        Set<TableMetadataStorage>(tables, "guildTable", new Lazy<GuildTable>(() => table));
        return tables;
    }

    private static GameSession Member(GuildWorld world, TableMetadataStorage tables, long characterId, Guild? guild) {
        var session = Bare<GameSession>();
        Set<GameSession>(session, "<World>k__BackingField", world);
        Set<GameSession>(session, "<TableMetadata>k__BackingField", tables);
        Set<Maple2.Server.Core.Network.Session>(session, "<CharacterId>k__BackingField", characterId);
        var manager = Bare<GuildManager>();
        Set<GuildManager>(manager, "session", session);
        Set<GuildManager>(manager, "logger", new LoggerConfiguration().CreateLogger());
        Set<GuildManager>(manager, "<Guild>k__BackingField", guild);
        session.Guild = manager;
        return session;
    }

    [Test]
    public void MassiveEvent_EachGuildGainsOnceAtItsLevel() {
        var world = new GuildWorld();
        TableMetadataStorage tables = Tables();
        var low = new Guild(39990001, "a", 1, 4000000001L, "a") { Experience = 5000 };
        var high = new Guild(39990002, "b", 1, 4000000003L, "b") { Experience = 12000 };
        GameSession[] players = [
            Member(world, tables, 4000000001L, low),
            Member(world, tables, 4000000002L, low),
            Member(world, tables, 4000000003L, high),
            Member(world, tables, 4000000004L, null),
        ];

        TriggerContext.GiveGuildExp(players, 2, InstanceType.massiveEvent);

        Assert.That(world.Requests.Select(request => request.GuildCase), Is.All.EqualTo(GuildRequest.GuildOneofCase.AddExpFund));
        Assert.That(world.Requests.Select(request => (request.RequestorId, request.AddExpFund.GuildId, request.AddExpFund.Exp, request.AddExpFund.Fund)),
            Is.EqualTo(new[] {
                (4000000001L, 39990001L, 2000, 400000),
                (4000000003L, 39990002L, 2500, 500000),
            }));
    }

    [Test]
    public void OtherType_AsksForNothing() {
        var world = new GuildWorld();
        TableMetadataStorage tables = Tables();
        var guild = new Guild(39990001, "a", 1, 4000000001L, "a");

        TriggerContext.GiveGuildExp([Member(world, tables, 4000000001L, guild)], 1, InstanceType.massiveEvent);

        Assert.That(world.Requests, Is.Empty);
    }

    // The arenas (65000001-65000003) and the wedding room (84000007) have no instancefield row: a game a player can
    // queue for again at once pays nothing. Nor does an instance field of another type.
    [TestCase(null)]
    [TestCase(InstanceType.DungeonLobby)]
    [TestCase(InstanceType.WeddingHall)]
    public void NotAMassiveEventField_AsksForNothing(InstanceType? fieldType) {
        var world = new GuildWorld();
        TableMetadataStorage tables = Tables();
        var guild = new Guild(39990001, "a", 1, 4000000001L, "a");

        TriggerContext.GiveGuildExp([Member(world, tables, 4000000001L, guild)], 2, fieldType);

        Assert.That(world.Requests, Is.Empty);
    }
}
