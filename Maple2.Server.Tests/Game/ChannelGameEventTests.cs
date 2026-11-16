using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using GameEventRequest = Maple2.Server.Channel.Service.GameEventRequest;

namespace Maple2.Server.Tests.Game;

// The World's game event notices to this channel (ChannelService.GameEvent): an event a GM starts reaches the
// players who loaded the events (GameEvent Add) and the channel lists it; an event that ends is taken out of their
// clients (GameEvent Remove). An id the event table does not list changes nothing.
public class ChannelGameEventTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int EventId = 130003;

    private TestGameSession test = null!;
    private GameSession session = null!;
    private GameServer server = null!;
    private ChannelService channel = null!;
    private GameEventMetadata metadata = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Start();
        session = test.Session;
        metadata = new GameEventMetadata(EventId, GameEventType.FreeAirTaxi, DateTime.Now.AddDays(-1), DateTime.Now.AddDays(30),
            TimeSpan.Zero, TimeSpan.Zero, [], new FreeAirTaxi(), "", "", "", "");
        test.SetServerTable(new GameEventTable(new Dictionary<int, GameEventMetadata> { [EventId] = metadata }));
        test.SetTable(new ReactorTable(new Dictionary<int, ReactorTable.Entry>()));
        server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;
        channel = new ChannelService(server, session.PlayerInfo, test.Storage, test.ServerTableMetadata, null!, null!);
        server.LoadEvents(session);
        test.TakeSent();
    }

    private void Notify(Action<GameEventRequest> set) {
        var request = new GameEventRequest();
        set(request);
        channel.GameEvent(request, null!).GetAwaiter().GetResult();
    }

    private string[] Sent() => test.TakeSent().Where(packet => TestGameSession.Op(packet) == Maple2.Server.Core.Constants.SendOp.GameEvent)
        .Select(Convert.ToHexString).ToArray();

    [Test]
    public void Add_TheEventReachesTheLoadedPlayers() {
        Notify(request => request.Add = new GameEventRequest.Types.Add { EventId = EventId });

        GameEvent? added = server.FindEvent(EventId);
        Assert.That(added, Is.Not.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(GameEventPacket.Add(added!).ToArray()) }));
    }

    [Test]
    public void Remove_TheEventLeavesTheirClients() {
        Notify(request => request.Add = new GameEventRequest.Types.Add { EventId = EventId });
        test.TakeSent();

        Notify(request => request.Remove = new GameEventRequest.Types.Remove { EventId = EventId });

        Assert.That(server.FindEvent(EventId), Is.Null);
        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(GameEventPacket.Remove(EventId).ToArray()) }));
    }

    // An id the table does not list, an update (the channel keeps the table's row) and an empty request change nothing.
    [Test]
    public void UnlistedUpdateOrEmpty_ChangeNothing() {
        Notify(request => request.Add = new GameEventRequest.Types.Add { EventId = EventId + 1 });
        Notify(request => request.Remove = new GameEventRequest.Types.Remove { EventId = EventId + 1 });
        Notify(request => request.Update = new GameEventRequest.Types.Update { EventId = EventId });
        Notify(_ => { });

        Assert.That(server.FindEvent(EventId), Is.Null);
        Assert.That(server.FindEvent(EventId + 1), Is.Null);
        Assert.That(Sent(), Is.Empty);
    }
}
