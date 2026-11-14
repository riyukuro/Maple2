using System;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game;

// A move to another game server that the World refuses (an RpcException, or an answer with no usable address): the
// client shows its move error box (CNetworkService::OnGameToGame) and the player plays on where they stand, as
// GameSession.Migrate and the channel change do. The dungeon lobby, a player on another channel and the global
// portal used to end the connection here, after the move save, which then was not stored again.
public class RefusedMoveTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private TestGameSession test = null!;
    private GameSession session = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        session = test.Start();
        test.EnterMap(TestGameSession.Map(2000023));
        // Saves as a live session does (TestGameSession.Start counts as saved), so the move save runs and stores.
        test.SavingConfig(session);
        typeof(GameSession).GetField("preMigrationSaved", Any)!.SetValue(session, false);
        test.TakeSent();
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    private bool Closing() => (int) typeof(Session).GetField("disconnecting", Any)!.GetValue(session)! == 1;

    private bool SavesAgain() => !(bool) typeof(GameSession).GetField("preMigrationSaved", Any)!.GetValue(session)!;

    private void Refuse(bool badAddress) {
        if (badAddress) {
            test.World.MoveAddress = "";
        } else {
            test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));
        }
    }

    private static MigrationError Error(bool badAddress) => badAddress ? MigrationError.s_move_err_default : MigrationError.s_move_err_no_server;

    private void AssertStays(bool badAddress) {
        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(Sent(), Is.EqualTo(new[] { Hex(MigrationPacket.GameToGameError(Error(badAddress))) }));
            Assert.That(Closing(), Is.False);
            Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeMap));
            Assert.That(SavesAgain(), Is.True);
        });
    }

    private PlayerInfo OnAnotherChannel() {
        test.World.PlayerWarpAnswer = new Maple2.PlayerWarpResponse { RoomId = 5, X = 1, Y = 2, Z = 3 };
        return new PlayerInfo(new CharacterInfo(20000000002, 20000000003, "x", "", "", Gender.Female, Job.Newbie, 10), "", new AchievementInfo(), []) {
            Channel = (short) (session.Player.Value.Character.Channel + 1),
            MapId = 2000062,
        };
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GoToPlayer_OnAnotherChannel_RefusedMoveStays(bool badAddress) {
        PlayerInfo target = OnAnotherChannel();
        Refuse(badAddress);

        Assert.That(PlayerWarpUtil.GoToPlayer(session, target), Is.False);

        AssertStays(badAddress);
    }

    [Test]
    public void GoToPlayer_OnAnotherChannel_MovesAndCloses() {
        PlayerInfo target = OnAnotherChannel();

        Assert.That(PlayerWarpUtil.GoToPlayer(session, target), Is.True);

        Assert.Multiple(() => {
            Assert.That(session.State, Is.EqualTo(SessionState.ChangeMap));
            Assert.That(Closing(), Is.True);
            Assert.That(SavesAgain(), Is.False);
        });
    }

    private void EnterGlobalPortal() {
        test.World.OpenPortal = new GlobalPortalInfo { EventId = 7, Channel = 1, MapId = 65000001, RoomId = 3, PortalId = 1 };
        new Maple2.Server.Game.PacketHandlers.GlobalPortal { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
            writer.WriteInt(7);
            writer.WriteInt(0);
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GlobalPortal_RefusedMoveStays(bool badAddress) {
        Refuse(badAddress);

        EnterGlobalPortal();

        AssertStays(badAddress);
    }

    // A World server that does not answer the join: nothing is sent, nothing is moved or saved, the player stays.
    [Test]
    public void GlobalPortal_WorldDownStays() {
        test.World.TimeEventFailure = new RpcException(new Status(StatusCode.Unavailable, "no world"));

        Assert.DoesNotThrow(EnterGlobalPortal);

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Is.Empty);
            Assert.That(test.TakeSent(), Is.Empty);
            Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeMap));
            Assert.That(Closing(), Is.False);
            Assert.That(SavesAgain(), Is.True);
        });
    }

    [Test]
    public void GlobalPortal_MovesAndCloses() {
        EnterGlobalPortal();

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1));
            Assert.That(session.State, Is.EqualTo(SessionState.ChangeMap));
            Assert.That(Closing(), Is.True);
        });
    }

    private void MigrateToDungeon() {
        session.Dungeon.Metadata = TestGameSession.DungeonRoom(23000001, 2000062);
        typeof(Maple2.Server.Game.Manager.DungeonManager).GetMethod("MigrateToDungeon", Any)!.Invoke(session.Dungeon, null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DungeonLobby_RefusedMoveStays(bool badAddress) {
        Refuse(badAddress);

        MigrateToDungeon();

        AssertStays(badAddress);
    }

    [Test]
    public void DungeonLobby_MovesAndCloses() {
        MigrateToDungeon();

        Assert.Multiple(() => {
            Assert.That(test.World.Moves.Single().MapId, Is.EqualTo(2000062));
            Assert.That(session.State, Is.EqualTo(SessionState.ChangeMap));
            Assert.That(Closing(), Is.True);
        });
    }
}
