using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game.Pvp;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client's Pvp requests (op 0x61). The rank duel goes to the World's queue (CPvpRankDuelComponent::OnMessage
// reads the answers); an arena request gets the answer that puts the client's state back to 0
// (CSceneStateGame::OnPvp).
public class PvpHandlerTests {
    private static List<string> Send(TestGameSession test, GameSession session, string hex) {
        test.TakeSent();
        new PvpHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private List<string> Send(string hex) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        return Send(test, session, hex);
    }

    // Register (14): Pvp 14 (state 1, the unregister button), then the World queues the character.
    [Test]
    public void RankDuelRegister_IsQueued() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "0E"), Is.EqualTo(new[] { "B600" + "0E" }));
        Assert.That(test.World.PvpRequests.Single().PvpCase, Is.EqualTo(PvpRequest.PvpOneofCase.Register));
        Assert.That(test.World.PvpRequests.Single().RequesterId, Is.EqualTo(test.CharacterId));
    }

    // A refused register (queued or in a match already) takes the state back to 0 with the matching failed box.
    [Test]
    public void RankDuelRegister_Refused_GetsTheFailedMatch() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Register] = new PvpResponse { Error = (int) RankDuelError.Busy };
        Assert.That(Send(test, session, "0E"), Is.EqualTo(new[] { "B600" + "0E", "B600" + "10" + "00" }));
    }

    [Test]
    public void RankDuelUnregister_LeavesTheQueue() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "0F"), Is.EqualTo(new[] { "B600" + "0F" }));
        Assert.That(test.World.PvpRequests.Single().PvpCase, Is.EqualTo(PvpRequest.PvpOneofCase.Unregister));
    }

    // Join (16) with no match: the matching failed box.
    [Test]
    public void RankDuelJoin_NoMatch_GetsTheFailedMatch() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Join] = new PvpResponse { Error = (int) RankDuelError.NoMatch };
        Assert.That(Send(test, session, "10"), Is.EqualTo(new[] { "B600" + "10" + "00" }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    // Join with a match: the character moves to the match field's channel, map and room.
    [Test]
    public void RankDuelJoin_MovesIntoTheMatchField() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Join] = new PvpResponse { Channel = 1, MapId = 65010004, RoomId = 321 };
        Send(test, session, "10");
        MigrateOutRequest move = test.World.Moves.Single();
        Assert.That((move.Channel, move.MapId, move.RoomId, move.Type), Is.EqualTo((1, 65010004, 321, MigrationType.PvpMatch)));
        Assert.That(move.CharacterId, Is.EqualTo(test.CharacterId));
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
