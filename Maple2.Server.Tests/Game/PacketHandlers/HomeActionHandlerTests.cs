using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
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
}
