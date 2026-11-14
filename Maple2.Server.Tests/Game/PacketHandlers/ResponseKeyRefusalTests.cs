using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The entry to a game server (ResponseKey) that the server refuses after the world let it in: the character does not
// load, say. The refusal was answered twice, by EnterServer and again by the handler, so the client showed its move
// error box twice before the connection closed.
public class ResponseKeyRefusalTests {
    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        session = test.Start();
        // The server's session tables EnterServer adds the session to (ResetSaveTests).
        object server = typeof(GameSession).GetField("server", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        Type gameServer = typeof(Maple2.Server.Game.GameServer);
        gameServer.GetField("mutex", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new object());
        gameServer.GetField("connectingSessions", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new HashSet<GameSession>());
        gameServer.GetField("sessions", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, new System.Collections.Concurrent.ConcurrentDictionary<long, GameSession>());
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private bool Ended => (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)! == 1;

    [Test]
    public void ARefusedEntry_IsAnsweredOnce() {
        // A character the world names that is not stored: the load fails.
        test.World.MigrateInAnswer = new MigrateInResponse { CharacterId = test.CharacterId + 1000, Channel = 1, MapId = 2000023 };
        test.TakeSent();

        new ResponseKeyHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(test.AccountId);
            writer.WriteLong(77);
            writer.WriteBytes(Guid.NewGuid().ToByteArray());
        }));

        bool ended = Ended; // read before TakeSent, which opens the session again
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.MoveResult }));
            Assert.That(ended, Is.True);
        });
    }

    // A returner (40 days away, KR 180005's 30) whose gift rows the database refuses: the load's transaction is
    // rolled back and the entry refused (GameSession.EnterServer), so neither the gift's mail nor its time is kept
    // and the load is not let in without them.
    [Test]
    public void ReturnerGiftNotStored_RefusesTheEntry() {
        using (var context = new Ms2Context(test.Options)) {
            Maple2.Database.Model.Character row = context.Character.Single(character => character.Id == test.CharacterId);
            context.Entry(row).Property(character => character.LastModified).CurrentValue = DateTime.UtcNow.AddDays(-40);
            context.SaveChanges();
        }
        test.Register(TestGameSession.Metadata(20303031));
        test.AddEvent(180005, GameEventType.ReturnUserYearRound, new ReturnUserYearRound(170531001, new RewardItem(20303031, 1, 1), 30, 90));
        object stats = typeof(TestGameSession).GetField("stats", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(test)!;
        typeof(GameSession).GetProperty(nameof(GameSession.ItemStatsCalc))!.SetValue(session, stats);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.AccountEventValue>().Any(entry => entry.State == EntityState.Added);
        test.World.MigrateInAnswer = new MigrateInResponse { CharacterId = test.CharacterId, Channel = 1, MapId = 2000023 };
        test.TakeSent();

        new ResponseKeyHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(test.AccountId);
            writer.WriteLong(77);
            writer.WriteBytes(Guid.NewGuid().ToByteArray());
        }));

        bool ended = Ended;
        test.FailSave = null;
        Assert.Multiple(() => {
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.MoveResult }));
            Assert.That(ended, Is.True);
            Assert.That(test.Errors, Has.Some.Contains("Returner gift"));
        });
        // The in-memory store keeps no transaction, so the mail made before the refused time stays there; the gift
        // time (the write refused) is not stored.
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.GetAccountEventValues(test.AccountId), Is.Empty);
    }
}
