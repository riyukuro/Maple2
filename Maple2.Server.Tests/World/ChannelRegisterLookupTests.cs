using Maple2.Server.World.Containers;

namespace Maple2.Server.Tests.World;

// How the World server's channel table registers a Game server: a free id is given out, a Game server that names
// its running channel gets it back, a second process asking for the same id is refused, and an instanced-content
// channel is always id 0 and not one of the server list. A channel starts pending: it is not active (not counted
// in the list) until its health check answers.
public class ChannelRegisterLookupTests {
    private const string Ip = "127.0.0.1";

    private static ChannelClientLookup Lookup() => new();

    [Test]
    public void FindOrCreate_GivesTheSmallestFreeIds() {
        ChannelClientLookup lookup = Lookup();
        Assert.That(lookup.FindOrCreateChannelByIp(Ip, Ip, false, "a").channel, Is.EqualTo(1));
        Assert.That(lookup.FindOrCreateChannelByIp(Ip, Ip, false, "b").channel, Is.EqualTo(2));
        Assert.That(lookup.FindOrCreateChannelByIp(Ip, Ip, true, "c").channel, Is.EqualTo(0));
        Assert.That(lookup.FindOrCreateChannelByIp(Ip, Ip, true, "d").channel, Is.EqualTo(-1));
    }

    [Test]
    public void ARegisteredChannel_IsListedButNotActiveYet() {
        ChannelClientLookup lookup = Lookup();
        (ushort gamePort, int grpcPort, int channel, bool known) = lookup.Register(Ip, Ip, false, 3, "game-a");
        Assert.Multiple(() => {
            Assert.That(channel, Is.EqualTo(3));
            Assert.That(known, Is.False);
            Assert.That(gamePort, Is.Not.Zero);
            Assert.That(grpcPort, Is.Not.Zero);
            Assert.That(lookup.ValidChannel(3), Is.True);
            Assert.That(lookup.HasListedChannel(3), Is.True);
            Assert.That(lookup.IsInstanced(3), Is.False);
            Assert.That(lookup.Count, Is.Zero);
            Assert.That(lookup.FirstChannel(), Is.EqualTo(-1));
            Assert.That(lookup.TryGetClient(3, out _), Is.True);
            Assert.That(lookup.TryGetActiveEndpoint(3, out var endpoint), Is.True);
            Assert.That(endpoint!.Port, Is.EqualTo(gamePort));
        });
    }

    [Test]
    public void TheSameGameServerAskingAgain_IsKnown() {
        ChannelClientLookup lookup = Lookup();
        lookup.Register(Ip, Ip, false, 3, "game-a");
        (_, _, int channel, bool known) = lookup.Register(Ip, Ip, false, 3, "game-a");
        Assert.That(channel, Is.EqualTo(3));
        Assert.That(known, Is.True);
    }

    [Test]
    public void AnotherGameServerAskingForTheId_IsRefused() {
        ChannelClientLookup lookup = Lookup();
        lookup.Register(Ip, Ip, false, 3, "game-a");
        (_, _, int channel, bool known) = lookup.Register(Ip, Ip, false, 3, "game-b");
        Assert.That(channel, Is.EqualTo(-1));
        Assert.That(known, Is.False);
    }

    [Test]
    public void AnInstancedChannel_IsNotOnTheServerList() {
        ChannelClientLookup lookup = Lookup();
        Assert.That(lookup.Register(Ip, Ip, true, 0, "dungeons").channel, Is.EqualTo(0));
        Assert.Multiple(() => {
            Assert.That(lookup.IsInstanced(0), Is.True);
            Assert.That(lookup.HasListedChannel(0), Is.False);
            Assert.That(lookup.Keys, Is.Empty);
            Assert.That(lookup.TryGetInstancedChannelId(out _), Is.False);
        });
    }

    [Test]
    public void AnUnknownChannel_HasNothing() {
        ChannelClientLookup lookup = Lookup();
        Assert.Multiple(() => {
            Assert.That(lookup.ValidChannel(-1), Is.False);
            Assert.That(lookup.ValidChannel(9), Is.False);
            Assert.That(lookup.IsInstanced(9), Is.False);
            Assert.That(lookup.HasListedChannel(9), Is.False);
            Assert.That(lookup.TryGetClient(9, out var client), Is.False);
            Assert.That(client, Is.Null);
            Assert.That(lookup.TryGetActiveEndpoint(9, out _), Is.False);
        });
    }

    [Test]
    public void ARequestedIdOfTheWrongKind_IsRefused() {
        ChannelClientLookup lookup = Lookup();
        Assert.That(lookup.Register(Ip, Ip, true, 2, "x").channel, Is.EqualTo(-1));
        Assert.That(lookup.Register(Ip, Ip, false, 0, "y").channel, Is.EqualTo(-1));
    }
}
