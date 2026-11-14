using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A portal out of a dungeon to a map outside it is a move to another server (GameSession.PrepareField hands it to
// Migrate). When the World server refuses it, the client gets the move's error box (CNetworkService::OnGameToGame)
// and stays; a FieldEnter error after it showed a second box for the same move.
public class DungeonExitRefusalTests {
    private const int Outside = 2000062;

    private static (TestGameSession Test, DungeonFieldManager Lobby) InADungeon() {
        var test = new TestGameSession();
        test.Start();
        var room = new DungeonRoomMetadata(23000001, 0, DungeonPlayType.none, DungeonGroupType.normal, DungeonCooldownType.none, 0, 0,
            LobbyFieldId: 2010001, FieldIds: [2010002], Reward: null!, Limit: null!, 0, 0, 3, false, 0, 0, false, [], [], false, default, false);
        test.EnterDungeon(room);
        test.AddRoomField(2010002);
        var lobby = (DungeonFieldManager) test.Session.Field!;
        FieldInfo portals = typeof(FieldManager).GetField("fieldPortals", BindingFlags.Instance | BindingFlags.NonPublic)!;
        portals.SetValue(lobby, Activator.CreateInstance(portals.FieldType));
        lobby.SpawnPortal(new Portal(2, Outside, 1, PortalType.Field, PortalActionType.Interact, Vector3.Zero, Vector3.Zero,
            new Vector3(100, 100, 100), 0, 0, true, true, true));
        test.TakeSent();
        return (test, lobby);
    }

    [Test]
    public void ARefusedMoveOutOfADungeon_ShowsOneErrorBox() {
        (TestGameSession test, DungeonFieldManager lobby) = InADungeon();
        test.World.MoveError = new RpcException(new Status(StatusCode.Unavailable, "refused"));

        lobby.UsePortal(test.Session, 2, "");

        Assert.Multiple(() => {
            Assert.That(test.World.Moves, Has.Count.EqualTo(1), "the move asked for");
            // The move's error box alone: no FieldEnter error for the same move.
            Assert.That(test.TakeSent().Select(Convert.ToHexString),
                Is.EqualTo(new[] { Convert.ToHexString(MigrationPacket.GameToGameError(MigrationError.s_move_err_no_server).ToArray()) }));
            Assert.That(test.Session.Field, Is.SameAs(lobby));
        });
    }

    // A field change that fails on this server still gets its FieldEnter error.
    [Test]
    public void AFailedFieldChangeOnThisServer_KeepsItsError() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.TakeSent();

        session.SendFieldEnter(false);

        Assert.That(test.TakeSent().Select(Convert.ToHexString),
            Is.EqualTo(new[] { Convert.ToHexString(FieldEnterPacket.Error(MigrationError.s_move_err_default).ToArray()) }));
    }
}
