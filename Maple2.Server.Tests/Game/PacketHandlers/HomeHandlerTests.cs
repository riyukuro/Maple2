using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestHome: accept 0 (the invited player's answer, passed on to the inviter as HomeInvite result), invite 1 and
// the go-home menu entry 3 (UIGameMenuDialog). The client shows one notice per HomeInvite result code
// (CSceneStateGame::OnHomeInviteResult, scenestategame.cpp:6637).
public class HomeHandlerTests {
    private TestGameSession playerTest = null!;
    private TestGameSession otherTest = null!;
    private GameSession player => playerTest.Session;
    private GameSession other => otherTest.Session;
    private HomeHandler handler = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        playerTest = new TestGameSession(options);
        otherTest = new TestGameSession(options);
        playerTest.AddMap(TestGameSession.Map(2000023));
        playerTest.Start();
        otherTest.Start();
        playerTest.EnterMap(TestGameSession.Map(2000023));
        handler = new HomeHandler { MapMetadataStorage = playerTest.MapMetadata, World = playerTest.World };
        // The session is made without its constructor, so its invite list is made here.
        foreach (GameSession session in new[] { player, other }) {
            typeof(GameSession).GetField("<HomeInvites>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, new Maple2.Server.Game.Util.HomeInviteTracker());
        }
        playerTest.TakeSent();
    }

    // Both sessions are on one server: each finds the other.
    private void Online(params GameSession[] sessions) {
        foreach (GameSession session in sessions) {
            var server = typeof(GameSession).GetField("server", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);
            var map = (ConcurrentDictionary<long, GameSession>) typeof(Maple2.Server.Game.GameServer)
                .GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
            foreach (GameSession member in sessions) {
                map[member.CharacterId] = member;
            }
        }
    }

    private void Invite(string name) {
        handler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteUnicodeString(name);
        }));
    }

    private void Accept(GameSession from, string inviter, byte answer) {
        handler.Handle(from, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteUnicodeString(inviter);
            writer.WriteByte(answer);
        }));
    }

    private static byte[] Bytes(Maple2.PacketLib.Tools.ByteWriter packet) => packet.ToArray();

    [Test]
    public void Invite_AnEmptyOrUnknownNameGetsTheInvalidUserBox() {
        player.Housing.InitNewHome("home", null);
        Invite("");
        Invite("nobody");

        byte[] box = Bytes(NoticePacket.MessageBox(StringCode.s_fail_enterfield_invaliduser));
        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { box, box }));
    }

    [Test]
    public void Invite_YourselfAnswersTheSelfNotice() {
        Invite(player.PlayerName.ToUpperInvariant());

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Self, string.Empty)) }));
    }

    [Test]
    public void Invite_WithoutASetUpHomeGetsTheInvalidStateBox() {
        Invite(other.PlayerName);

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_home_returnable_invalid_state)) }));
    }

    [Test]
    public void Invite_AnOfflineCharacterAnswersOffline() {
        player.Housing.InitNewHome("home", null);

        Invite(other.PlayerName);

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Offline, other.PlayerName)) }));
    }

    [Test]
    public void Invite_ReachesTheOnlinePlayerAndTheirAnswerReachesTheInviter() {
        player.Housing.InitNewHome("home", null);
        Online(player, other);
        otherTest.TakeSent();

        Invite(other.PlayerName);

        Assert.That(otherTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeInvitePacket.Invite(player.Player.Value.Character)) }));
        Assert.That(playerTest.TakeSent(), Is.Empty);

        Accept(other, player.PlayerName, 1);

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeInvitePacket.Result(HomeInvitePacket.InviteResult.Accepted, other.PlayerName)) }));
        // The invite is used up by the answer: a second one is not passed on.
        Accept(other, player.PlayerName, 2);
        Assert.That(playerTest.TakeSent(), Is.Empty);
    }

    [TestCase((byte) 2)]
    [TestCase((byte) 5)]
    [TestCase((byte) 6)]
    public void Accept_EachAnswerCodeIsPassedOnAsIs(byte answer) {
        player.Housing.InitNewHome("home", null);
        Online(player, other);
        Invite(other.PlayerName);
        playerTest.TakeSent();

        Accept(other, player.PlayerName, answer);

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(HomeInvitePacket.Result((HomeInvitePacket.InviteResult) answer, other.PlayerName)) }));
    }

    [Test]
    public void Accept_WithoutAnInviteOrWithAnUnknownCodeTellsNobody() {
        player.Housing.InitNewHome("home", null);
        Online(player, other);
        Accept(other, player.PlayerName, 1);
        Invite(other.PlayerName);
        playerTest.TakeSent();

        Accept(other, player.PlayerName, 3);
        Accept(other, "", 1);
        Accept(other, other.PlayerName, 1);
        Accept(other, "nobody", 1);

        Assert.That(playerTest.TakeSent(), Is.Empty);
    }

    [Test]
    public void Warp_ADeadPlayerGetsTheInvalidStateBox() {
        typeof(Maple2.Server.Game.Manager.ConfigManager).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player.Config, player);
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetProperty("IsDead")!.GetSetMethod(true)!.Invoke(player.Player, [true]);
        handler.Handle(player, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(player.Player.IsDead, Is.True);
        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_home_returnable_invalid_state)) }));
        Assert.That(playerTest.World.Moves, Is.Empty);
    }

    [Test]
    public void Warp_FromAMapThatIsNotReturnableGetsTheForbiddenBox() {
        playerTest.EnterMap(TestGameSession.Map(2000023, homeReturnable: false));

        handler.Handle(player, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_home_returnable_forbidden)) }));
    }

    [Test]
    public void Warp_InYourOwnHomeGetsTheSamePlaceBox() {
        player.Housing.InitNewHome("home", null);
        playerTest.EnterHome();
        playerTest.TakeSent();

        handler.Handle(player, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(playerTest.TakeSent(), Is.EqualTo(new[] { Bytes(NoticePacket.MessageBox(StringCode.s_home_returnable_forbidden_to_sameplace)) }));
    }

    [Test]
    public void Warp_ASetUpHomeMovesToItsMap() {
        player.Housing.InitNewHome("home", null);

        handler.Handle(player, TestGameSession.Packet(writer => writer.WriteByte(3)));

        Assert.That(playerTest.World.Moves, Has.Count.EqualTo(1));
        Assert.That(playerTest.World.Moves[0].MapId, Is.EqualTo(player.Player.Value.Home.Indoor.MapId));
    }

    [Test]
    public void Warp_ARefusedTemplateIdMovesNobody_AndAValidOneNamesTheHome() {
        handler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(7);
        }));
        Assert.That(playerTest.World.Moves, Is.Empty);
        Assert.That(player.Player.Value.Home.IsHomeSetup, Is.False);

        handler.Handle(player, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteInt(-1);
        }));

        Assert.That(player.Player.Value.Home.IsHomeSetup, Is.True);
        Assert.That(player.Player.Value.Home.Indoor.Name, Is.EqualTo(player.PlayerName));
        Assert.That(playerTest.World.Moves, Has.Count.EqualTo(1));
    }
}
