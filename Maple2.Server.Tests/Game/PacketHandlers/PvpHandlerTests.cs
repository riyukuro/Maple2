using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game.Pvp;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The client's Pvp requests (op 0x61). The rank duel goes to the World's queue (CPvpRankDuelComponent::OnMessage
// reads the answers); an arena request gets the answer that puts the client's state back to 0
// (CSceneStateGame::OnPvp).
public class PvpHandlerTests {
    private static readonly Dictionary<int, SeasonDataTable.Entry> NoSeasons = [];

    // The rows of table/individualarena.xml.
    internal static readonly IndividualArenaTable Arenas = new(new Dictionary<int, IndividualArenaTable.Entry> {
        [1] = new(1, 65000001, 10, 40100023, 1000, 200, 0, 0, 0),
        [2] = new(2, 65000002, 10, 40100023, 100, 10, 0, 0, 0),
        [3] = new(3, 65000003, 20, 40100023, 1000, 400, 0, 0, 0),
    });

    // A session with no PvP season and no rank duel record (the register reads the rating).
    private static TestGameSession NewTest() {
        var test = new TestGameSession();
        test.SetTable(new SeasonDataTable(NoSeasons, NoSeasons, NoSeasons, NoSeasons, NoSeasons, NoSeasons, NoSeasons, NoSeasons, NoSeasons));
        test.SetTable(Arenas);
        return test;
    }

    private static List<string> Send(TestGameSession test, GameSession session, string hex) {
        test.TakeSent();
        new PvpHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteBytes(Convert.FromHexString(hex))));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static List<string> Send(string hex) {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        return Send(test, session, hex);
    }

    // Register (14): Pvp 14 (state 1, the unregister button), then the World queues the character.
    [Test]
    public void RankDuelRegister_IsQueued() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "0E"), Is.EqualTo(new[] { "B600" + "0E" }));
        Assert.That(test.World.PvpRequests.Single().PvpCase, Is.EqualTo(PvpRequest.PvpOneofCase.Register));
        // No record yet: the start rating goes to the queue.
        Assert.That(test.World.PvpRequests.Single().Register.Rating, Is.EqualTo(RankDuelRecord.StartRating));
        Assert.That(test.World.PvpRequests.Single().RequesterId, Is.EqualTo(test.CharacterId));
    }

    // Queued already: the World keeps the character queued, so state 1 stays (no failed box).
    [Test]
    public void RankDuelRegister_QueuedAlready_StaysQueued() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Register] = new PvpResponse { Error = (int) RankDuelError.Queued };
        Assert.That(Send(test, session, "0E"), Is.EqualTo(new[] { "B600" + "0E" }));
    }

    // A dead player is refused before the World is asked: the failed box, state 0.
    [TestCase("0E")]
    [TestCase("10")]
    public void RankDuel_Dead_IsRefused(string request) {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        typeof(Maple2.Server.Game.Model.FieldPlayer)
            .GetField("isDead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Player, true);
        Assert.That(Send(test, session, request), Is.EqualTo(new[] { "B600" + "10" + "00" }));
        Assert.That(test.World.PvpRequests, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // From a dungeon the register and the join are refused before the World is asked.
    [TestCase("0E")]
    [TestCase("10")]
    public void RankDuel_FromADungeon_IsRefused(string request) {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.EnterDungeon(TestGameSession.DungeonRoom(23000001, 2000062));
        Assert.That(Send(test, session, request), Is.EqualTo(new[] { "B600" + "10" + "00" }));
        Assert.That(test.World.PvpRequests, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // The move into the match field fails after the World's join: the client leaves state 2 (failed box).
    [Test]
    public void RankDuelJoin_MoveFails_GetsTheFailedMatch() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Join] = new PvpResponse { Channel = 1, MapId = 65010004, RoomId = 321 };
        test.World.MoveAddress = "";
        List<string> sent = Send(test, session, "10");
        Assert.That(sent.Last(), Is.EqualTo("B600" + "10" + "00"));
    }

    // A refused register (in a match already) takes the state back to 0 with the matching failed box.
    [Test]
    public void RankDuelRegister_Refused_GetsTheFailedMatch() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Register] = new PvpResponse { Error = (int) RankDuelError.Busy };
        Assert.That(Send(test, session, "0E"), Is.EqualTo(new[] { "B600" + "0E", "B600" + "10" + "00" }));
    }

    [Test]
    public void RankDuelUnregister_LeavesTheQueue() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "0F"), Is.EqualTo(new[] { "B600" + "0F" }));
        Assert.That(test.World.PvpRequests.Single().PvpCase, Is.EqualTo(PvpRequest.PvpOneofCase.Unregister));
    }

    // Join (16) with no match: the matching failed box.
    [Test]
    public void RankDuelJoin_NoMatch_GetsTheFailedMatch() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Join] = new PvpResponse { Error = (int) RankDuelError.NoMatch };
        Assert.That(Send(test, session, "10"), Is.EqualTo(new[] { "B600" + "10" + "00" }));
        Assert.That(test.World.Moves, Is.Empty);
    }

    // Join with a match: the character moves to the match field's channel, map and room.
    [Test]
    public void RankDuelJoin_MovesIntoTheMatchField() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.Join] = new PvpResponse { Channel = 1, MapId = 65010004, RoomId = 321 };
        Send(test, session, "10");
        MigrateOutRequest move = test.World.Moves.Single();
        Assert.That((move.Channel, move.MapId, move.RoomId, move.Type), Is.EqualTo((1, 65010004, 321, MigrationType.PvpMatch)));
        Assert.That(move.CharacterId, Is.EqualTo(test.CharacterId));
    }

    // Arena register (9 with the arena): queued with its playerCount, then Pvp 12 with the World's waits (arenaType,
    // milliseconds) for every arena of the table, 0 for an empty queue. An arena that is not in the table gets Pvp 10, the client's state 0.
    [Test]
    public void ArenaRegister_IsQueued() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        var response = new PvpResponse();
        response.ArenaWaits.Add(1, 4000);
        test.World.PvpResponses[PvpRequest.PvpOneofCase.ArenaRegister] = response;
        Assert.That(Send(test, session, "09" + "01000000"), Is.EqualTo(new[] {
            "B600" + "09" + "01000000",
            "B600" + "0C" + "03" + "01000000" + "A00F0000" + "02000000" + "00000000" + "03000000" + "00000000",
        }));
        PvpRequest request = test.World.PvpRequests.Single();
        Assert.That((request.ArenaRegister.Arena, request.ArenaRegister.PlayerCount), Is.EqualTo((1, 10)));

        test.World.PvpRequests.Clear();
        Assert.That(Send(test, session, "09" + "04000000"), Is.EqualTo(new[] { "B600" + "0A" }));
        Assert.That(test.World.PvpRequests, Is.Empty);
    }

    [Test]
    public void ArenaUnregister_LeavesTheQueue() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        Assert.That(Send(test, session, "0A"), Is.EqualTo(new[] { "B600" + "0A",
            "B600" + "0C" + "03" + "01000000" + "00000000" + "02000000" + "00000000" + "03000000" + "00000000" }));
        Assert.That(test.World.PvpRequests.Single().PvpCase, Is.EqualTo(PvpRequest.PvpOneofCase.ArenaUnregister));
    }

    // Join (11): with no match Pvp 10; with one the character moves to the arena field's room.
    [Test]
    public void ArenaJoin() {
        TestGameSession test = NewTest();
        GameSession session = test.Start();
        test.World.PvpResponses[PvpRequest.PvpOneofCase.ArenaJoin] = new PvpResponse { Error = (int) RankDuelError.NoMatch };
        Assert.That(Send(test, session, "0B"), Is.EqualTo(new[] { "B600" + "0A" }));
        test.World.PvpResponses[PvpRequest.PvpOneofCase.ArenaJoin] = new PvpResponse { Channel = 1, MapId = 65000002, RoomId = 77 };
        Send(test, session, "0B");
        MigrateOutRequest move = test.World.Moves.Single();
        Assert.That((move.MapId, move.RoomId, move.Type), Is.EqualTo((65000002, 77, MigrationType.PvpMatch)));
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
