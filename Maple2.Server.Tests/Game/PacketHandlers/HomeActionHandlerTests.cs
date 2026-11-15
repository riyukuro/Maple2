using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// HomeAction commands 6 (portal cube settings), 12 (notice cube settings) and 13 (the dialog's settings request): the
// owner of the plot edits the settings of a cube that has them; the client's dialog limits (HomeEditRules) hold.
public class HomeActionHandlerTests {
    private static readonly Vector3B PortalCell = new(1, 2, 3);
    private static readonly Vector3B OtherPortalCell = new(4, 2, 3);
    private static readonly Vector3B NoticeCell = new(5, 2, 3);
    private static readonly Vector3B PlainCell = new(6, 2, 3);

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private HomeActionHandler handler = null!;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Start();
        plot = test.EnterHome();
        // The field is made without its constructor: the portal list a portal edit walks is made here.
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField("fieldPortals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session.Field, new System.Collections.Concurrent.ConcurrentDictionary<int, Maple2.Server.Game.Model.FieldPortal>());
        AddCube(PortalCell, ConfigurableCubeType.UGCPortal);
        AddCube(OtherPortalCell, ConfigurableCubeType.UGCPortal).Interact!.PortalSettings!.PortalName = "taken";
        AddCube(NoticeCell, ConfigurableCubeType.UGCNotice);
        AddCube(PlainCell, ConfigurableCubeType.None);
        handler = new HomeActionHandler();
        test.TakeSent();
    }

    private PlotCube AddCube(Vector3B cell, ConfigurableCubeType type) {
        long id = 100 + plot.Cubes.Count;
        var cube = new PlotCube(TestGameSession.Metadata(Constant.InteriorPortalCubeId), id) { Position = cell, Type = PlotCube.CubeType.Default };
        cube.Interact = new InteractCube(cell, new FunctionCubeMetadata(Constant.InteriorPortalCubeId, 0, type,
            InteractCubeState.Available, InteractCubeControlType.None, [], 0, null));
        plot.Cubes[cell] = cube;
        return cube;
    }

    private void Portal(Vector3B cell, string name, byte method = 0, byte destination = 0, string target = "") {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(6);
            writer.WriteByte(0);
            writer.Write<Vector3B>(cell);
            writer.WriteUnicodeString(name);
            writer.WriteByte(method);
            writer.WriteByte(destination);
            writer.WriteUnicodeString(target);
        }));
    }

    private void Notice(Vector3B cell, string notice, byte distance) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteByte(0);
            writer.Write<Vector3B>(cell);
            writer.WriteUnicodeString(notice);
            writer.WriteByte(distance);
        }));
    }

    private void Request(Vector3B cell) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(13);
            writer.Write<Vector3B>(cell);
        }));
    }

    private CubePortalSettings PortalSettings => plot.Cubes[PortalCell].Interact!.PortalSettings!;
    private CubeNoticeSettings NoticeSettings => plot.Cubes[NoticeCell].Interact!.NoticeSettings!;

    [Test]
    public void Portal_Saved() {
        Portal(PortalCell, "gate", 1, 1, "2000001"); // touch, selected map

        Assert.Multiple(() => {
            Assert.That(PortalSettings.PortalName, Is.EqualTo("gate"));
            Assert.That((byte) PortalSettings.Method, Is.EqualTo(1));
            Assert.That((byte) PortalSettings.Destination, Is.EqualTo(1));
            Assert.That(PortalSettings.DestinationTarget, Is.EqualTo("2000001"));
        });
    }

    [TestCase("taken", 0, 0, TestName = "Portal_NameOfAnotherPortal_Refused")]
    [TestCase("a-name-that-is-far-too-long-for-the-dialog-a-name-that-is-far-too-long-for-the-dialog", 0, 0, TestName = "Portal_NameTooLong_Refused")]
    [TestCase("gate", 2, 0, TestName = "Portal_MethodOutOfRange_Refused")]
    [TestCase("gate", 0, 3, TestName = "Portal_DestinationOutOfRange_Refused")]
    public void Portal_Refused_KeepsTheSettings(string name, byte method, byte destination) {
        Portal(PortalCell, name, method, destination);

        Assert.That(PortalSettings.PortalName, Is.Not.EqualTo(name));
    }

    [Test]
    public void Portal_CubeWithoutPortalSettings_Ignored() {
        Portal(NoticeCell, "gate");
        Portal(PlainCell, "gate");
        Portal(new Vector3B(9, 9, 9), "gate");

        Assert.That(NoticeSettings.Notice, Is.Empty);
    }

    [Test]
    public void Portal_OtherPlayersPlot_Ignored() {
        plot.OwnerId = session.AccountId + 1;

        Portal(PortalCell, "gate");

        Assert.That(PortalSettings.PortalName, Is.Not.EqualTo("gate"));
    }

    [Test]
    public void Notice_Saved_BroadcastToTheField() {
        Notice(NoticeCell, "hello", 3);

        Assert.Multiple(() => {
            Assert.That(NoticeSettings.Notice, Is.EqualTo("hello"));
            Assert.That(NoticeSettings.Distance, Is.EqualTo(3));
            Assert.That(test.TakeSent().Select(p => Convert.ToHexString(p.ToArray())),
                Does.Contain(Convert.ToHexString(HomeActionPacket.SendCubeNoticeSettings(plot.Cubes[NoticeCell], editing: false).ToArray())));
        });
    }

    [Test]
    public void Notice_TooLong_Refused() {
        Notice(NoticeCell, new string('x', 1025), 1);

        Assert.That(NoticeSettings.Notice, Is.Empty);
    }

    [Test]
    public void Notice_CubeWithoutNoticeSettings_Ignored() {
        Notice(PortalCell, "hello", 1);
        Notice(new Vector3B(9, 9, 9), "hello", 1);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Request_PortalCube_AnswersWithTheOtherNames() {
        Request(PortalCell);

        // The list holds every other portal cube's name: the plain cubes carry the portal item id here too, so theirs is empty.
        List<string> others = plot.Cubes.Values.Where(x => x.Id != plot.Cubes[PortalCell].Id)
            .Select(x => x.Interact!.PortalSettings?.PortalName ?? string.Empty).ToList();
        byte[] expected = HomeActionPacket.SendCubePortalSettings(plot.Cubes[PortalCell], others).ToArray();
        Assert.That(test.TakeSent().Select(p => Convert.ToHexString(p.ToArray())), Does.Contain(Convert.ToHexString(expected)));
    }

    [Test]
    public void Request_NoticeCube_AnswersEditing() {
        Request(NoticeCell);

        Assert.That(test.TakeSent().Select(p => Convert.ToHexString(p.ToArray())),
            Does.Contain(Convert.ToHexString(HomeActionPacket.SendCubeNoticeSettings(plot.Cubes[NoticeCell], editing: true).ToArray())));
    }

    [Test]
    public void Request_PlainCube_Silent() {
        Request(PlainCell);
        Request(new Vector3B(9, 9, 9));

        Assert.That(test.TakeSent(), Is.Empty);
    }

    // ---- survey (command 5): the owner's started survey; a visitor's answer is kept once and ends it at the last one.

    private const long SurveyId = 77;

    private HomeSurvey StartSurvey(params string[] voters) {
        var survey = new HomeSurvey(SurveyId, "best?", publicQuestion: true);
        survey.Options["a"] = [];
        survey.Options["b"] = [];
        survey.AvailableCharacters = voters.ToList();
        survey.Started = true;
        survey.MaxAnswers = voters.Length;
        ((HomeFieldManager) session.Field!).SetHomeSurvey(survey);
        test.TakeSent();
        return survey;
    }

    private void Answer(long surveyId, byte index) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteByte(0);
            writer.WriteLong(session.CharacterId);
            writer.WriteLong(surveyId);
            writer.WriteByte(index);
        }));
    }

    private List<string> SentHex() => test.TakeSent().Select(p => Convert.ToHexString(p.ToArray())).ToList();

    [Test]
    public void Survey_Answer_KeptAndAnswered() {
        HomeSurvey survey = StartSurvey(session.PlayerName, "someone");

        Answer(SurveyId, 1);

        Assert.Multiple(() => {
            Assert.That(survey.Options["b"], Is.EqualTo(new[] { session.PlayerName }));
            Assert.That(survey.AvailableCharacters, Is.EqualTo(new[] { "someone" }));
            Assert.That(survey.Answers, Is.EqualTo(1));
            Assert.That(survey.Ended, Is.False);
            Assert.That(SentHex(), Is.EqualTo(new[] { Convert.ToHexString(HomeActionPacket.SurveyAnswer(session.PlayerName).ToArray()) }));
        });
    }

    [Test]
    public void Survey_LastAnswer_EndsTheSurveyWithTheResult() {
        HomeSurvey survey = StartSurvey(session.PlayerName);

        Answer(SurveyId, 0);

        // The end packet is built from the survey as it stood before End() emptied the question.
        Assert.Multiple(() => {
            Assert.That(survey.Ended, Is.True);
            Assert.That(survey.Started, Is.False);
            Assert.That(SentHex(), Has.Count.EqualTo(2));
        });
    }

    [TestCase(78L, (byte) 0, TestName = "Survey_OtherSurveyId_Ignored")]
    [TestCase(SurveyId, (byte) 5, TestName = "Survey_OptionOutOfRange_Ignored")]
    public void Survey_Refused_KeepsTheSurvey(long surveyId, byte index) {
        HomeSurvey survey = StartSurvey(session.PlayerName);

        Answer(surveyId, index);

        Assert.Multiple(() => {
            Assert.That(survey.Answers, Is.EqualTo(0));
            Assert.That(survey.AvailableCharacters, Is.EqualTo(new[] { session.PlayerName }));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Survey_NotInvited_Ignored() {
        HomeSurvey survey = StartSurvey("someone");

        Answer(SurveyId, 0);

        Assert.That(survey.Options["a"], Is.Empty);
    }

    [Test]
    public void Survey_SecondAnswer_Ignored() {
        HomeSurvey survey = StartSurvey(session.PlayerName, "someone");
        Answer(SurveyId, 0);
        test.TakeSent();

        Answer(SurveyId, 0);

        Assert.Multiple(() => {
            Assert.That(survey.Answers, Is.EqualTo(1));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void Survey_NotStarted_Ignored() {
        HomeSurvey survey = StartSurvey(session.PlayerName);
        survey.Started = false;

        Answer(SurveyId, 0);

        Assert.That(survey.Answers, Is.EqualTo(0));
    }

    // ---- ball (command 7): the owner's moves go to the others; a hit goes to the ball's owner.

    private const int BallId = 4242;
    private static readonly Vector3 Coord = new(10, 20, 30);
    private static readonly Vector3 Velocity = new(1, 2, 3);
    private static readonly Vector3 Angular = new(4, 5, 6);

    private FieldGuideObject GiveBall() {
        var ball = new FieldGuideObject(session.Field!, BallId, new BallGuideObject(1f)) { CharacterId = session.CharacterId };
        session.GuideObject = ball;
        session.Field!.Players[session.Player.ObjectId] = session.Player;
        test.TakeSent();
        return ball;
    }

    private void BallPacket(byte mode, int objectId, Vector3 a, Vector3 b, Vector3? c = null) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteByte(mode);
            writer.WriteInt(objectId);
            writer.Write<Vector3>(a);
            writer.Write<Vector3>(b);
            if (c.HasValue) {
                writer.Write<Vector3>(c.Value);
            }
        }));
    }

    [Test]
    public void Ball_Move_UpdatesThePosition() {
        FieldGuideObject ball = GiveBall();

        BallPacket(2, BallId, Coord, Velocity, Angular);

        Assert.That(ball.Position, Is.EqualTo(Coord));
    }

    [TestCase(9999, TestName = "Ball_Move_OtherObjectId_Ignored")]
    public void Ball_Move_NotTheOwnersBall_Ignored(int objectId) {
        FieldGuideObject ball = GiveBall();
        Vector3 before = ball.Position;

        BallPacket(2, objectId, Coord, Velocity, Angular);

        Assert.That(ball.Position, Is.EqualTo(before));
    }

    [Test]
    public void Ball_Move_WithoutABall_Ignored() {
        BallPacket(2, BallId, Coord, Velocity, Angular);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Ball_Hit_GoesToTheBallsOwner() {
        FieldGuideObject ball = GiveBall();

        BallPacket(3, BallId, Velocity, Angular);

        Assert.That(SentHex(), Does.Contain(Convert.ToHexString(HomeActionPacket.HitBall(ball, Velocity, Angular).ToArray())));
    }

    [Test]
    public void Ball_Hit_NonFiniteForce_Dropped() {
        GiveBall();

        BallPacket(3, BallId, new Vector3(float.NaN, 0, 0), Angular);

        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Ball_Hit_UnknownBall_Dropped() {
        GiveBall();

        BallPacket(3, 9999, Velocity, Angular);

        Assert.That(test.TakeSent(), Is.Empty);
    }
}
