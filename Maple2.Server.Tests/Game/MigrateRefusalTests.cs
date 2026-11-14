using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A move to another game server (a portal, a home, a guild field, the home planner) that the World server refuses or
// answers without an address: the client shows its move error box (CNetworkService::OnGameToGame, kind 2 with an
// error) and plays on, so the session stays in its field, as on a refused channel change. A move made ends the session.
public class MigrateRefusalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Target = 2000062;

    // The close of the session (Disconnect or Cut); TestGameSession.TakeSent clears it.
    private static bool Ended(GameSession session) =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", Any)!.GetValue(session)! == 1;

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private static List<string> Sent(TestGameSession test) => test.TakeSent().Select(Convert.ToHexString).ToList();

    [TestCase(StatusCode.Unavailable, MigrationError.s_move_err_no_server)]
    [TestCase(StatusCode.ResourceExhausted, MigrationError.s_move_err_member_limit)]
    [TestCase(StatusCode.Internal, MigrationError.s_move_err_default)]
    public void Migrate_RefusedByTheWorld_KeepsTheSession(StatusCode status, MigrationError error) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        int mapBefore = session.Player.Value.Character.MapId;
        SessionState state = session.State;
        test.World.MoveError = new RpcException(new Status(status, "refused"));
        test.TakeSent();

        session.Migrate(Target);

        bool ended = Ended(session); // read before TakeSent, which opens the send queue again
        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1), "the move asked for");
            Assert.That(Sent(test), Is.EqualTo(new[] { Hex(MigrationPacket.GameToGameError(error)) }));
            Assert.That(ended, Is.False, "the session ended");
            Assert.That(session.State, Is.EqualTo(state));
            Assert.That(session.Player.Value.Character.MapId, Is.EqualTo(mapBefore));
        });
    }

    [Test]
    public void Migrate_NoAddress_KeepsTheSession() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.MoveAddress = "";
        test.TakeSent();

        session.Migrate(Target);

        bool ended = Ended(session); // read before TakeSent, which opens the send queue again
        Assert.Multiple(() => {
            Assert.That(Sent(test), Is.EqualTo(new[] { Hex(MigrationPacket.GameToGameError(MigrationError.s_move_err_default)) }));
            Assert.That(ended, Is.False, "the session ended");
            Assert.That(test.Errors, Has.Count.EqualTo(1), "the World's answer logged");
        });
    }

    [Test]
    public void MigrateToPlanner_RefusedByTheWorld_KeepsTheSession() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        SessionState state = session.State;
        test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));
        test.TakeSent();

        session.MigrateToPlanner(PlotMode.DecorPlanner);

        bool ended = Ended(session); // read before TakeSent, which opens the send queue again
        Assert.Multiple(() => {
            Assert.That(Sent(test), Is.EqualTo(new[] { Hex(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server)) }));
            Assert.That(ended, Is.False, "the session ended");
            Assert.That(session.State, Is.EqualTo(state));
        });
    }

    [Test]
    public void Migrate_Made_EndsTheSession() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        session.Migrate(Target);

        bool ended = Ended(session); // read before TakeSent, which opens the send queue again
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.GameToGame }));
            Assert.That(ended, Is.True);
        });
    }
}
