using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// A move whose save does not store is not made (GameSession.MigrationSave): the client was sent to a server that
// loaded the last stored state, while the items made since were stored at once and the ones used up were not. The
// save is tried twice; a second failure ends the session, as a failed SessionSave does, and asks the World for no move.
public class MigrationSaveRefusalTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Target = 2000062;

    private static bool Ended(GameSession session) =>
        (int) typeof(Maple2.Server.Core.Network.Session).GetField("disconnecting", Any)!.GetValue(session)! == 1;

    // A session whose saves run (TestGameSession marks the move's save as made).
    private static GameSession Live(TestGameSession test) {
        GameSession session = test.Start();
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        session.State = SessionState.Connected;
        test.TakeSent();
        return session;
    }

    // As Live, with the session's Dispose run whole (the last save and the World's offline update), as
    // SessionCloseSaveTests sets it up.
    private static GameSession Closable(TestGameSession test) {
        GameSession session = Live(test);
        var server = (GameServer) typeof(GameSession).GetField("server", Any)!.GetValue(session)!;
        var sessions = new ConcurrentDictionary<long, GameSession>();
        sessions[session.CharacterId] = session;
        typeof(GameServer).GetField("sessions", Any)!.SetValue(server, sessions);
        typeof(GameServer).GetField("mutex", Any)!.SetValue(server, new object());
        typeof(GameServer).GetField("connectingSessions", Any)!.SetValue(server, new HashSet<GameSession>());
        session.GroupChats = new ConcurrentDictionary<int, GroupChatManager>();
        session.Clubs = new ConcurrentDictionary<long, ClubManager>();
        typeof(GameSession).GetField(nameof(GameSession.ItemLockStaging), Any)!.SetValue(session, new long[18]);
        typeof(GameSession).GetField(nameof(GameSession.DismantleStaging), Any)!.SetValue(session, new (long Uid, int Amount)[100]);
        typeof(GameSession).GetField(nameof(GameSession.Scheduler), Any)!.SetValue(session, new EventQueue(Serilog.Log.Logger));
        session.Party = (PartyManager) RuntimeHelpers.GetUninitializedObject(typeof(PartyManager));
        typeof(GameSession).GetField("gameDisposeState", Any)!.SetValue(session, 0);
        // Off the map: the harness field is only half built, and the moves and the save need none.
        session.Field = null;
        return session;
    }

    // Every save throws, or only the first `times` ones.
    private static void FailSaves(TestGameSession test, int times = int.MaxValue) {
        int failed = 0;
        test.FailSave = _ => failed++ < times;
    }

    [Test]
    public void FailedSave_IsFalse_AndEndsTheSession() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test);

        Assert.That(session.MigrationSave(), Is.False);
        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.False, "the session's next save is made");
            Assert.That(session.DropsPackets, Is.False);
            Assert.That(Ended(session), Is.True);
        });
    }

    [Test]
    public void SaveRefusedOnce_SecondTryStores() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test, times: 1);

        Assert.That(session.MigrationSave(), Is.True);
        Assert.Multiple(() => {
            Assert.That(session.MigrationSaved, Is.True);
            Assert.That(Ended(session), Is.False);
        });
    }

    [Test]
    public void Migrate_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        int mapBefore = session.Player.Value.Character.MapId;
        FailSaves(test);

        session.Migrate(Target);

        AssertEndedOffline(test, session);
        Assert.That(session.Player.Value.Character.MapId, Is.EqualTo(mapBefore), "the last save stores the map the player is on");
    }

    // The session the failed save ends is told offline to the World (Dispose sends it for a session not in ChangeMap)
    // and stays closed: the move's finally does not set it back to its state before the move.
    private static void AssertEndedOffline(TestGameSession test, GameSession session) {
        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(Ended(session), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(session.MigrationSaved, Is.False);
            Assert.That(test.World.PlayerUpdates.Count(update => update.CharacterId == session.CharacterId && update.Channel == -1), Is.EqualTo(1),
                "the World sets the player offline");
        });
    }

    [Test]
    public void MigrateToPlanner_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        FailSaves(test);

        session.MigrateToPlanner(PlotMode.Normal);

        AssertEndedOffline(test, session);
    }

    // The second save (what beforeMove changed, after the World gave the move) does not store: the return position
    // beforeMove set is put back before the session's last save, and the map too. Asserted on the character row each
    // save writes, so on the last one (Dispose's), not on the character after the move's finally put it back.
    [Test]
    public void Migrate_SecondSaveFails_BeforeMoveUndone() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        int mapBefore = character.MapId;
        var returnBefore = new System.Numerics.Vector3(1, 2, 3);
        character.ReturnPosition = returnBefore;
        bool afterMove = false;
        var written = new List<(int MapId, System.Numerics.Vector3 Return)>();
        test.FailSave = context => {
            foreach (var row in context.ChangeTracker.Entries<Maple2.Database.Model.Character>()
                         .Where(row => row.Entity.Id == session.CharacterId)) {
                written.Add((row.Entity.MapId, new System.Numerics.Vector3(row.Entity.Profile.ReturnX, row.Entity.Profile.ReturnY, row.Entity.Profile.ReturnZ)));
            }
            return afterMove;
        };

        session.Migrate(Target, beforeMove: () => {
            character.ReturnPosition = default;
            afterMove = true;
        });

        Assert.That(written, Has.Count.GreaterThanOrEqualTo(4), "the first save, the second one's two tries, the last save");
        Assert.Multiple(() => {
            Assert.That(written[0].MapId, Is.EqualTo(Target), "the first save stores the move");
            Assert.That(written[^1].MapId, Is.EqualTo(mapBefore), "the last save stores the map the player is on");
            Assert.That(written[^1].Return, Is.EqualTo(returnBefore), "the last save stores the return position before the move");
            Assert.That(character.ReturnPosition, Is.EqualTo(returnBefore));
            Assert.That(character.MapId, Is.EqualTo(mapBefore));
        });
        // The World was asked for the move (its answer came before beforeMove); the client is not sent on.
        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(Ended(session), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(test.World.PlayerUpdates.Count(update => update.CharacterId == session.CharacterId && update.Channel == -1), Is.EqualTo(1));
        });
    }

    // A payment beforeMove kept (a paid move's fare, GameSession.PaidFieldChange's pay) is given back by moveUndone when
    // the save after it does not store: once, and before the session's last save, which so stores the wallet with
    // the fare back and the character on the map it did not leave. Before, the fare stayed taken with no move.
    [Test]
    public void Migrate_SecondSaveFails_PaymentGivenBackBeforeTheLastSave() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        session.Currency.Meso = 10000;
        bool afterMove = false;
        int givenBack = 0;
        var written = new List<long>();
        test.FailSave = context => {
            foreach (var row in context.ChangeTracker.Entries<Maple2.Database.Model.Character>()
                         .Where(row => row.Entity.Id == session.CharacterId)) {
                written.Add(row.Entity.Currency.Meso);
            }
            return afterMove;
        };

        session.Migrate(Target, beforeMove: () => {
            Assert.That(session.Currency.TryTakeMeso(4000, notify: false), Is.True);
            afterMove = true;
        }, moveUndone: () => {
            givenBack++;
            session.Currency.ReturnMeso(4000, notify: false);
        });

        Assert.That(written, Has.Count.GreaterThanOrEqualTo(4), "the first save, the second one's two tries, the last save");
        Assert.Multiple(() => {
            Assert.That(givenBack, Is.EqualTo(1));
            Assert.That(written[^1], Is.EqualTo(10000), "the last save stores the fare given back");
            Assert.That(session.Currency.Meso, Is.EqualTo(10000));
            Assert.That(Ended(session), Is.True);
        });
    }

    // The second save's commit throws and its outcome cannot be read (no mark in memory): the move may be stored
    // whole, or nothing of it, and nothing more is stored. The session is ended only after the undo: the payment is
    // given back and the session's state put back first, so Dispose tells the World the player is offline (a session
    // in ChangeMap is one that left). Before, LockedSave cut the session first: Dispose found it in ChangeMap with the
    // target map and the fare taken, and the World was not told.
    [Test]
    public void Migrate_SecondSaveCommitUnknown_UndoneBeforeTheCut() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        int mapBefore = session.Player.Value.Character.MapId;
        int givenBack = 0;
        bool endedAtGiveBack = true;

        try {
            session.Migrate(Target, beforeMove: () => {
                Maple2.Database.Storage.DatabaseRequest<Maple2.Database.Context.Ms2Context>.CommitFault = after => {
                    if (after) {
                        Maple2.Database.Storage.DatabaseRequest<Maple2.Database.Context.Ms2Context>.CommitFault = null;
                        throw new InvalidOperationException("commit answer lost");
                    }
                };
            }, moveUndone: () => {
                givenBack++;
                endedAtGiveBack = Ended(session);
            });
        } finally {
            Maple2.Database.Storage.DatabaseRequest<Maple2.Database.Context.Ms2Context>.CommitFault = null;
        }

        Assert.Multiple(() => {
            Assert.That(givenBack, Is.EqualTo(1));
            Assert.That(endedAtGiveBack, Is.False, "given back before the cut");
            Assert.That(Ended(session), Is.True);
            Assert.That(session.Player.Value.Character.MapId, Is.EqualTo(mapBefore));
            Assert.That(session.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(test.World.PlayerUpdates.Count(update => update.CharacterId == session.CharacterId && update.Channel == -1), Is.EqualTo(1),
                "the World sets the player offline");
        });
    }

    // A move that went keeps the payment: moveUndone does not run.
    [Test]
    public void Migrate_PaidMoveThatWent_KeepsThePayment() {
        var test = new TestGameSession();
        GameSession session = Closable(test);
        int givenBack = 0;

        session.Migrate(Target, beforeMove: () => { }, moveUndone: () => givenBack++);

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(givenBack, Is.Zero);
        });
    }

    [Test]
    public void Migrate_SaveRefusedOnce_Moves() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test, times: 1);

        session.Migrate(Target);

        Assert.That(test.World.Moves, Has.Count.EqualTo(1));
    }

    [Test]
    public void ChannelChange_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        FailSaves(test);

        new ChannelHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteShort(1)));

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(session.State, Is.EqualTo(SessionState.Connected));
        });
    }

    [Test]
    public void QuitToCharacterList_FailedSave_NoMove() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        int mapBefore = character.MapId;
        character.ReturnMaps.Push(Target);
        FailSaves(test);

        new QuitHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteBool(false)));

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(Ended(session), Is.True);
            Assert.That(character.MapId, Is.EqualTo(mapBefore), "the return map the quit set is put back");
        });
    }

    // A quit the World refuses after the save stored puts the return map back too: the session's last save stores
    // the map the character is on.
    [Test]
    public void QuitToCharacterList_WorldRefuses_MapPutBack() {
        var test = new TestGameSession();
        GameSession session = Live(test);
        Maple2.Model.Game.Character character = session.Player.Value.Character;
        int mapBefore = character.MapId;
        character.ReturnMaps.Push(Target);
        test.World.MoveError = new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gone"));

        new QuitHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteBool(false)));

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1), "the save stored and the World was asked");
            Assert.That(character.MapId, Is.EqualTo(mapBefore), "the return map the quit set is put back");
        });
    }
}
