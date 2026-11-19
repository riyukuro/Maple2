using System;
using System.Reflection;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Core;

// A packet no handler takes is dropped with a warning line: one per op per session, at most UnhandledOpLog.MaxLines
// ops a session, and the line counts the op's packets on the server so far.
public class UnhandledOpLogTests {
    [Test]
    public void OneLinePerOp_UpToTheCap() {
        var log = new UnhandledOpLog();
        Assert.Multiple(() => {
            Assert.That(log.ShouldLog(RecvOp.Rank), Is.True);
            Assert.That(log.ShouldLog(RecvOp.Rank), Is.False);
            Assert.That(log.ShouldLog(RecvOp.Gamble), Is.True);
        });

        for (int op = 0x1000; log.ShouldLog((RecvOp) op); op++) { }
        Assert.That(log.ShouldLog((RecvOp) 0x2000), Is.False);
    }

    [Test]
    public void Router_CountsUnhandledPackets_AndHandsThemToNoHandler() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        // Start marks the move's save as done, which drops every packet (GameSession.DropsPackets).
        typeof(GameSession).GetField("preMigrationSaved", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(session, false);
        var router = new PacketRouter<GameSession>(Array.Empty<PacketHandler<GameSession>>());

        router.OnPacket(session, TestGameSession.Packet(writer => writer.WriteShort((short) RecvOp.Rank)));
        router.OnPacket(session, TestGameSession.Packet(writer => writer.WriteShort((short) RecvOp.Rank)));
        router.OnPacket(session, TestGameSession.Packet(writer => writer.WriteShort((short) RecvOp.Gamble)));

        Assert.Multiple(() => {
            Assert.That(router.UnhandledCount(RecvOp.Rank), Is.EqualTo(2));
            Assert.That(router.UnhandledCount(RecvOp.Gamble), Is.EqualTo(1));
            Assert.That(router.UnhandledCount(RecvOp.Banword), Is.EqualTo(0));
            // The router logged both ops for this session already.
            Assert.That(session.UnhandledOps.ShouldLog(RecvOp.Rank), Is.False);
            Assert.That(session.UnhandledOps.ShouldLog(RecvOp.Gamble), Is.False);
        });
    }
}
