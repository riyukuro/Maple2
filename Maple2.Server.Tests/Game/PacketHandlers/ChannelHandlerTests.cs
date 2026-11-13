using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The channel change (Channel 0x003B, i16 channel). The client asks for it only for another channel, from a normal
// field, out of combat and alive (WorldService::FUN_141bc80c0); a refused move answers GameToGame with the error, so
// the client shows its move error box and the player plays on (CNetworkService::OnGameToGame). The test server is
// channel 0, so channel 1 is another channel.
public class ChannelHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static List<string> Send(TestGameSession test, GameSession session, short channel) {
        test.TakeSent();
        new ChannelHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => writer.WriteShort(channel)));
        return test.TakeSent().Select(Convert.ToHexString).ToList();
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void AnotherChannel_MovesThere() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.That(Send(test, session, 1), Has.Count.EqualTo(1));
        MigrateOutRequestAssert(test, 1);
        Assert.That(session.State, Is.EqualTo(SessionState.ChangeChannel));
    }

    private static void MigrateOutRequestAssert(TestGameSession test, int channel) {
        var move = test.World.Moves.Single();
        Assert.That(move.Channel, Is.EqualTo(channel));
        Assert.That(move.ExactChannel, Is.True);
    }

    // What the client never asks for: no move and no answer.
    [Test]
    public void FromADungeon_IsRefused() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.EnterDungeon(TestGameSession.DungeonRoom(23000001, 2000062));
        typeof(FieldManager).GetField("<FieldType>k__BackingField", Any)!.SetValue(session.Field, FieldType.Dungeon);
        Assert.That(Send(test, session, 1), Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void InCombat_IsRefused() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        session.Player.InBattle = true;
        Assert.That(Send(test, session, 1), Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void Dead_IsRefused() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);
        Assert.That(Send(test, session, 1), Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void OwnChannel_IsRefused() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        Assert.That(Send(test, session, 0), Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    // The World refuses the exact channel (it is gone or full): the move error, the channel list, and the player stays.
    [TestCase(StatusCode.Unavailable, MigrationError.s_move_err_no_server)]
    [TestCase(StatusCode.ResourceExhausted, MigrationError.s_move_err_member_limit)]
    [TestCase(StatusCode.Internal, MigrationError.s_move_err_default)]
    public void WorldRefusal_SendsTheMoveError(StatusCode status, MigrationError error) {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.MoveError = new RpcException(new Status(status, "refused"));
        test.World.ChannelList.Remove(1);
        Assert.That(Send(test, session, 1), Is.EqualTo(new[] {
            Hex(MigrationPacket.GameToGameError(error)),
            Hex(ChannelPacket.Load(new List<int> { 2 })),
        }));
        MigrateOutRequestAssert(test, 1);
        Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeChannel));
        Assert.That(typeof(GameSession).GetField("preMigrationSaved", Any)!.GetValue(session), Is.False);
    }

    [Test]
    public void BadEndpoint_SendsTheMoveError() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        test.World.MoveAddress = "";
        List<string> sent = Send(test, session, 1);
        Assert.That(sent.First(), Is.EqualTo(Hex(MigrationPacket.GameToGameError(MigrationError.s_move_err_default))));
        Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeChannel));
    }
}
