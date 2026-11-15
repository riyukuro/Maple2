using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// A map entered for the first time goes to the client's visited maps with FindFields (int count, the map ids;
// CSceneStateGame::OnFindFields). The world map draws a tile's icons, world bosses included, only on a visited tile;
// before, a map first entered this session stayed dark until the next login's ServerEnter.
public class MapVisitTests {
    private const int MapId = 2000260;

    [Test]
    public void FirstVisit_SendsTheMapToTheVisitedMaps() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Unlock.Maps.Remove(MapId);
        test.TakeSent();

        Assert.That(session.MarkVisited(MapId), Is.True);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        var reader = new ByteReader(sent[0]);
        Assert.Multiple(() => {
            Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.FindFields));
            Assert.That(reader.ReadInt(), Is.EqualTo(1));
            Assert.That(reader.ReadInt(), Is.EqualTo(MapId));
            Assert.That(reader.Available, Is.EqualTo(0));
            Assert.That(session.Player.Value.Unlock.Maps, Does.Contain(MapId));
        });
    }

    [Test]
    public void LaterVisit_SendsNothing() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.Value.Unlock.Maps.Add(MapId);
        test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(session.MarkVisited(MapId), Is.False);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }
}
