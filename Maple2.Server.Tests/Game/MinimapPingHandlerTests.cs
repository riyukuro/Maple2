using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The minimap ping packet (RecvOp.MinimapPing): the ping goes back to the sender with the session's own character id,
// only on a field whose data allows pings, and not twice within the minimum interval.
public class MinimapPingHandlerTests {
    private TestGameSession test = null!;
    private MinimapPingHandler handler = null!;

    private void Setup(bool allowed) {
        test = new TestGameSession();
        test.Start();
        MapMetadata map = TestGameSession.Map(2000001);
        map = map with { Property = map.Property with { MinimapPing = allowed } };
        test.EnterMap(map);
        test.Session.Party = new Maple2.Server.Game.Manager.PartyManager(test.World, test.Session);
        handler = new MinimapPingHandler();
        test.TakeSent();
    }

    private void Ping(byte kind, float x = 10, float y = 20, float z = 30, long claimedId = 999) {
        handler.Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteLong(claimedId);
            writer.WriteByte(kind);
            writer.WriteFloat(x);
            writer.WriteFloat(y);
            writer.WriteFloat(z);
        }));
    }

    [Test]
    public void Ping_OnAnAllowedFieldComesBackWithTheSessionsOwnId() {
        Setup(true);

        Ping(1);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0], Is.EqualTo(MinimapPingPacket.Ping(test.Session.CharacterId, 1, new Vector3(10, 20, 30)).ToArray()));
    }

    [Test]
    public void Ping_OnAFieldWithoutPingsSendsNothing() {
        Setup(false);

        Ping(0);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Ping_UnknownKindSendsNothing() {
        Setup(true);

        Ping(2);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Ping_NonFinitePositionSendsNothing() {
        Setup(true);

        Ping(0, float.NaN);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Ping_TwiceAtOnceAnswersOnlyTheFirst() {
        Setup(true);

        Ping(0);
        Ping(0);

        Assert.That(test.TakeSent(), Has.Count.EqualTo(1));
    }
}
