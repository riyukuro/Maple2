using System;
using System.Collections.Generic;
using Grpc.Core;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Core;

public class WorldLinkTests {
    // A World server as a Game server sees it: channels and online players in memory only.
    private class FakeWorld {
        public bool Up = true;
        public readonly Dictionary<int, string> Channels = new();
        public readonly Dictionary<long, int> Online = new();
        public readonly HashSet<int> Parties = [];

        public void Restart() {
            Channels.Clear();
            Online.Clear();
            Parties.Clear();
        }

        // The first registration: the World server picks the id.
        public int Add(bool instanced, string instance) {
            int id = ChannelRegisterRules.Pick(Channels.Keys, instanced, null);
            Channels[id] = instance;
            return id;
        }

        public (int Channel, bool Known) Register(bool instanced, int requested, string instance) {
            if (!Up) {
                throw new RpcException(new Status(StatusCode.Unavailable, "down"));
            }
            if (Channels.TryGetValue(requested, out string? holder)) {
                return ChannelRegisterRules.SameServer(holder, instance) ? (requested, true) : (-1, false);
            }

            int id = ChannelRegisterRules.Pick(Channels.Keys, instanced, requested);
            if (id >= 0) {
                Channels[id] = instance;
            }
            return (id, false);
        }
    }

    // A Game server with its sessions (character id, party id).
    private class FakeGame {
        public readonly string Instance = Guid.NewGuid().ToString("N");
        public readonly Dictionary<long, int> Sessions = new();
        public readonly List<long> Disbanded = [];
        public int Announces;
        public int Channel;
        public WorldLink Link = null!;

        public static FakeGame Start(FakeWorld world, bool instanced = false) {
            var game = new FakeGame();
            game.Channel = world.Add(instanced, game.Instance);
            game.Link = new WorldLink(game.Channel, () => world.Register(instanced, game.Channel, game.Instance), () => {
                game.Announces++;
                foreach ((long characterId, int partyId) in game.Sessions) {
                    if (!world.Up) {
                        throw new RpcException(new Status(StatusCode.Unavailable, "down"));
                    }
                    world.Online[characterId] = game.Channel;
                    if (ChannelRegisterRules.PartyGone(partyId, world.Parties.Contains(partyId) ? partyId : 0)) {
                        game.Disbanded.Add(characterId);
                    }
                }
                foreach (long characterId in game.Disbanded) {
                    game.Sessions[characterId] = 0;
                }
                return true;
            });
            return game;
        }
    }

    [Test]
    public void AKnownChannelIsLeftAlone() {
        var world = new FakeWorld();
        FakeGame game = FakeGame.Start(world);
        game.Sessions[100] = 0;

        Assert.That(game.Link.Check(), Is.EqualTo(WorldLink.Step.Known));
        Assert.That(game.Link.Check(), Is.EqualTo(WorldLink.Step.Known));
        Assert.That(game.Announces, Is.EqualTo(0));
    }

    [Test]
    public void ARestartedWorldGetsTheSameChannelIdAndTheSessions() {
        var world = new FakeWorld();
        FakeGame first = FakeGame.Start(world);
        FakeGame second = FakeGame.Start(world);
        Assert.That((first.Channel, second.Channel), Is.EqualTo((1, 2)));
        second.Sessions[200] = 0;
        second.Sessions[201] = 0;

        world.Up = false;
        Assert.That(second.Link.Check(), Is.EqualTo(WorldLink.Step.Down));
        Assert.That(second.Announces, Is.EqualTo(0));

        // The World server comes back empty; channel 2 answers first and still gets id 2, not 1.
        world.Restart();
        world.Up = true;
        Assert.That(second.Link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(world.Channels.Keys, Is.EquivalentTo(new[] { 2 }));
        Assert.That(world.Online, Is.EquivalentTo(new Dictionary<long, int> { [200] = 2, [201] = 2 }));
        Assert.That(second.Announces, Is.EqualTo(1));

        Assert.That(first.Link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(world.Channels.Keys, Is.EquivalentTo(new[] { 1, 2 }));

        // Known from then on: the sessions are reported once.
        Assert.That(second.Link.Check(), Is.EqualTo(WorldLink.Step.Known));
        Assert.That(second.Announces, Is.EqualTo(1));
    }

    [Test]
    public void TheInstancedContentChannelKeepsIdZero() {
        var world = new FakeWorld();
        FakeGame instanced = FakeGame.Start(world, instanced: true);
        Assert.That(instanced.Channel, Is.EqualTo(0));

        world.Restart();
        Assert.That(instanced.Link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(world.Channels.Keys, Is.EquivalentTo(new[] { 0 }));
    }

    [Test]
    public void AnIdHeldByAnotherGameServerIsRefused() {
        var world = new FakeWorld();
        FakeGame old = FakeGame.Start(world);
        old.Sessions[300] = 0;

        // The World server restarts and a new Game server gets id 1 before the old one asks.
        world.Restart();
        FakeGame other = FakeGame.Start(world);
        Assert.That(other.Channel, Is.EqualTo(1));

        Assert.That(old.Link.Check(), Is.EqualTo(WorldLink.Step.Refused));
        Assert.That(old.Announces, Is.EqualTo(0));
        Assert.That(world.Online, Is.Empty);
        Assert.That(other.Link.Check(), Is.EqualTo(WorldLink.Step.Known));
    }

    [Test]
    public void AReportThatFailsIsTriedAgain() {
        var world = new FakeWorld();
        FakeGame game = FakeGame.Start(world);
        game.Sessions[400] = 0;
        world.Restart();

        bool fail = true;
        int calls = 0;
        var link = new WorldLink(game.Channel, () => world.Register(false, game.Channel, game.Instance), () => {
            calls++;
            if (fail) {
                throw new RpcException(new Status(StatusCode.Unavailable, "down"));
            }
            return true;
        });

        Assert.That(link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(calls, Is.EqualTo(1));
        fail = false;
        Assert.That(link.Check(), Is.EqualTo(WorldLink.Step.Known));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(link.Check(), Is.EqualTo(WorldLink.Step.Known));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void PartiesEndWithTheWorldServer() {
        var world = new FakeWorld();
        FakeGame game = FakeGame.Start(world);
        world.Parties.Add(7);
        game.Sessions[500] = 7;
        game.Sessions[501] = 7;
        game.Sessions[502] = 0;

        world.Restart();
        Assert.That(game.Link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(game.Disbanded, Is.EquivalentTo(new long[] { 500, 501 }));
    }

    // The World server lives and only dropped the channel: its parties are still there and are kept.
    [Test]
    public void APartyTheWorldServerStillHasIsKept() {
        var world = new FakeWorld();
        FakeGame game = FakeGame.Start(world);
        world.Parties.Add(7);
        game.Sessions[500] = 7;

        world.Channels.Clear();
        Assert.That(game.Link.Check(), Is.EqualTo(WorldLink.Step.Registered));
        Assert.That(game.Disbanded, Is.Empty);
    }

    [TestCase(false, null, 1)]
    [TestCase(false, 3, 3)]
    [TestCase(false, 2, -1)] // taken
    [TestCase(false, 0, -1)] // 0 is the instanced-content channel
    [TestCase(true, 1, -1)]
    [TestCase(true, null, 0)]
    [TestCase(true, 0, 0)]
    [TestCase(false, -1, -1)]
    public void ChannelIds(bool instanced, int? requested, int expected) {
        Assert.That(ChannelRegisterRules.Pick(new HashSet<int> { 2 }, instanced, requested), Is.EqualTo(expected));
    }

    [Test]
    public void TheSmallestFreeIdIsGiven() {
        Assert.That(ChannelRegisterRules.Pick(new HashSet<int> { 0, 1, 2, 4 }, false, null), Is.EqualTo(3));
        Assert.That(ChannelRegisterRules.Pick(new HashSet<int> { 0 }, true, null), Is.EqualTo(-1));
    }

    [TestCase("a", "a", true)]
    [TestCase("a", "b", false)]
    [TestCase("", "", false)]
    public void TheSameProcessHoldsAChannel(string registered, string asking, bool expected) {
        Assert.That(ChannelRegisterRules.SameServer(registered, asking), Is.EqualTo(expected));
    }

    [TestCase(0, 0, false)]
    [TestCase(7, 7, false)]
    [TestCase(7, 0, true)]
    [TestCase(7, 8, true)]
    public void PartyGone(int held, int world, bool expected) {
        Assert.That(ChannelRegisterRules.PartyGone(held, world), Is.EqualTo(expected));
    }
}
