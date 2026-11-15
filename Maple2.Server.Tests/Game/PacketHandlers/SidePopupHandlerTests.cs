using System;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// SidePopup command 1 (UISidePopupTalk onEnterFieldWar): the button of the field war announce. The packet has no war
// id, so the war is the one the player was announced (FieldWarUid). The enter checks again what the announce did, and
// a dead player stays. It joins the war at the World server and moves the player to the war's field.
public class SidePopupHandlerTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Uid = 1234567890123L;
    private const int WarMap = 2020061;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Setup(MapType type = MapType.None, short level = 70, int requireAchievementId = 0, bool announced = true) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            FieldWarRequireLevel = 70,
            FieldWarRequireAchieveID = requireAchievementId,
        });
        test.Start();
        session.Player.Value.Character.Level = level;
        MapMetadata map = TestGameSession.Map(2000001);
        test.EnterMap(map with { Property = map.Property with { Type = type } });
        test.World.OpenFieldWar = new FieldWarInfo { EventId = Uid, FieldWarId = 10000001, MapId = WarMap, Channel = 3, RoomId = 7 };
        if (announced) {
            session.FieldWarUid = Uid;
        }
        test.TakeSent();
    }

    private void Press(byte command = 1) {
        new SidePopupHandler { World = test.World }.Handle(session, TestGameSession.Packet(w => w.WriteByte(command)));
    }

    [Test]
    public void Enter_JoinsTheWarAndMoves() {
        Setup();
        Press();

        var join = test.World.FieldWarJoins.Single();
        Assert.That(join.EventId, Is.EqualTo(Uid));
        Assert.That(join.CharacterId, Is.EqualTo(session.CharacterId));
        var move = test.World.Moves.Single();
        Assert.That(move.MapId, Is.EqualTo(WarMap));
        Assert.That(move.Channel, Is.EqualTo(3));
        Assert.That(move.RoomId, Is.EqualTo(7));
        Assert.That(move.CharacterId, Is.EqualTo(session.CharacterId));
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.GameToGame));
        Assert.That(session.State, Is.EqualTo(SessionState.ChangeMap));
    }

    [Test]
    public void Enter_AnswersTheMapAndNoError() {
        Setup();
        Press();
        var reader = new ByteReader(test.TakeSent().First(p => TestGameSession.Op(p) == SendOp.GameToGame), 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
    }

    [Test]
    public void NotAnnounced_Ignored() {
        Setup(announced: false);
        Press();
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void UnknownCommand_Ignored() {
        Setup();
        Press(0);
        Press(2);
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void Dead_Stays() {
        Setup();
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", Any)!.SetValue(session.Player, true);
        Press();
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void LevelDropped_Stays() {
        Setup();
        session.Player.Value.Character.Level = 69;
        Press();
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void TrophyMissing_Stays() {
        Setup(requireAchievementId: 22200490);
        Press();
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [TestCase(MapType.Dungeon)]
    [TestCase(MapType.FieldWar)]
    [TestCase(MapType.Pvp)]
    public void FieldTypeWithoutAnnounce_Stays(MapType type) {
        Setup(type);
        Press();
        Assert.That(test.World.FieldWarJoins, Is.Empty);
        Assert.That(test.World.Moves, Is.Empty);
    }

    [Test]
    public void WarGone_NoMove() {
        Setup();
        test.World.OpenFieldWar = null;
        Press();
        Assert.That(test.World.FieldWarJoins, Has.Count.EqualTo(1));
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void WorldError_NoMove() {
        Setup();
        test.World.TimeEventError = 1;
        Press();
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void WorldDown_NoMove() {
        Setup();
        test.World.TimeEventFailure = new RpcException(new Status(StatusCode.Unavailable, "down"));
        Press();
        Assert.That(test.World.Moves, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void MoveRefused_SendsErrorAndNotice() {
        Setup();
        test.World.MoveError = new RpcException(new Status(StatusCode.NotFound, "channel gone"));
        Press();
        var sent = test.TakeSent().Select(TestGameSession.Op).ToList();
        Assert.That(sent, Does.Contain(SendOp.GameToGame));
        Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeMap));
    }

    [Test]
    public void EmptyAddress_SendsErrorOnly() {
        Setup();
        test.World.MoveAddress = "";
        Press();
        var packets = test.TakeSent();
        Assert.That(packets.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.GameToGame }));
        var reader = new ByteReader(packets[0], 0);
        reader.ReadShort();
        Assert.That(reader.ReadByte(), Is.Not.EqualTo(0));
        Assert.That(session.State, Is.Not.EqualTo(SessionState.ChangeMap));
    }
}
