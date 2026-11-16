using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Maple2.PacketLib.Tools;
using Maple2.Server.Channel.Service;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// What a plot change from another channel (ChannelService.UpdateFieldPlot) does before it reaches a field: a
// request from this channel is ignored, a request with no map is refused, one with no kind changes nothing, and
// the expiry notice goes only to the owner of a forfeit.
public class FieldPlotRequestTests {
    private ChannelService channel = null!;

    [SetUp]
    public void SetUp() {
        var server = (GameServer) RuntimeHelpers.GetUninitializedObject(typeof(GameServer));
        typeof(GameServer).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(server, new ConcurrentDictionary<long, GameSession>());
        channel = new ChannelService(server, null!, new TestGameSession().Storage, null!, null!, null!);
    }

    [Test]
    public void FromThisChannel_IsIgnored() {
        var request = new FieldPlotRequest { IgnoreChannel = GameServer.GetChannel(), MapId = 0 };

        Assert.That(channel.UpdateFieldPlot(request, null!).GetAwaiter().GetResult(), Is.Not.Null);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void WithoutAMap_IsRefused(int mapId) {
        var request = new FieldPlotRequest { IgnoreChannel = (short) (GameServer.GetChannel() + 1), MapId = mapId };

        var error = Assert.Throws<RpcException>(() => channel.UpdateFieldPlot(request, null!));
        Assert.That(error!.StatusCode, Is.EqualTo(StatusCode.InvalidArgument));
    }

    [Test]
    public void WithoutAKind_ChangesNothing() {
        var request = new FieldPlotRequest { IgnoreChannel = (short) (GameServer.GetChannel() + 1), MapId = 2000062 };

        Assert.That(channel.UpdateFieldPlot(request, null!).GetAwaiter().GetResult(), Is.Not.Null);
    }

    [TestCase(false, 5L)]
    [TestCase(true, 0L)]
    [TestCase(true, -1L)]
    [TestCase(false, 0L)]
    public void ExpiredNotice_OnlyForAForfeitWithAnOwner(bool forfeit, long account) {
        var update = new FieldPlotRequest.Types.UpdatePlot { Forfeit = forfeit, AccountId = account };

        Assert.That(ChannelService.ExpiredNotice(2000062, 3, update), Is.Null);
    }

    [Test]
    public void ExpiredNotice_IsTheConfirmedForfeitOfThePlot() {
        var update = new FieldPlotRequest.Types.UpdatePlot { Forfeit = true, AccountId = 5 };

        byte[] packet = ChannelService.ExpiredNotice(2000062, 3, update)!.ToArray();

        Assert.That(TestGameSession.Op(packet), Is.EqualTo(SendOp.ResponseCube));
        var reader = new ByteReader(packet, 3);
        Assert.That(reader.ReadShort(), Is.EqualTo(Maple2.Server.Game.Packets.CubePacket.ForfeitExpired));
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(2000062));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
    }
}
