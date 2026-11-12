using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client's Pvp requests (op 0x61) against a server with no arena queue and no rank duel: each queue
// request gets the answer that puts the client's state back to 0 (CSceneStateGame::OnPvp,
// CPvpRankDuelComponent::OnMessage).
public class PvpHandlerTests {
    private readonly PvpHandler handler = new();

    private List<string> Send(string hex) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();
        handler.Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    // Rank duel register (14) and the join after a match (16): Pvp 16 with false, the client's matching
    // failed box, and its state 0.
    [TestCase("0E")]
    [TestCase("10")]
    public void RankDuelRegister_IsAnsweredWithTheFailedMatch(string request) {
        Assert.That(Send(request), Is.EqualTo(new[] { "B600" + "10" + "00" }));
    }

    [Test]
    public void RankDuelUnregister_IsAnswered() {
        Assert.That(Send("0F"), Is.EqualTo(new[] { "B600" + "0F" }));
    }

    // Arena register (9 with the arena), unregister (10) and join (11): Pvp 10, the client's state 0.
    [TestCase("09" + "01000000")]
    [TestCase("0A")]
    [TestCase("0B")]
    public void ArenaRequests_AreAnsweredWithTheUnregisteredState(string request) {
        Assert.That(Send(request), Is.EqualTo(new[] { "B600" + "0A" }));
    }

    // Guild PvP (1 to 4) and the result dialogs' close (104, 110) have no answer.
    [TestCase("01")]
    [TestCase("02")]
    [TestCase("03")]
    [TestCase("04")]
    [TestCase("68")]
    [TestCase("6E")]
    public void RequestsWithoutAnAnswer_SendNothing(string request) {
        Assert.That(Send(request), Is.Empty);
    }
}
