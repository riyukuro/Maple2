using Maple2.Server.Core.Packets;
using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 46 (save the home to a layout slot) and 48 (load a slot): a slot above the last or below 0 is not saved;
// saving to a slot again replaces its layout and answers the saved one with the home's own size; loading is refused
// while the indoor still holds cubes (a notice), for a slot with no layout (the db error) and otherwise sets the home to
// the layout's size.
public class HomeLayoutSlotTests {
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.Start();
        session.Housing.InitNewHome("home", null);
        plot = test.EnterHome();
        session.Field!.Players.Clear();
        test.TakeSent();
    }

    private void Save(int slot, string name) => Send(46, writer => { writer.WriteInt(slot); writer.WriteUnicodeString(name); });
    private void Load(int slot) => Send(48, writer => writer.WriteInt(slot));

    private void Send(byte command, Action<ByteWriter> body) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            body(writer);
        }));
    }

    private string[] Sent() => test.TakeSent().Select(Convert.ToHexString).ToArray();

    [TestCase(-1)]
    [TestCase(Constant.HomeMaxLayoutSlots + 1)]
    public void Save_ToASlotOutOfRange_IsIgnored(int slot) {
        Save(slot, "x");

        Assert.That(session.Player.Value.Home.Layouts, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Save_KeepsTheHomesSize_AndAnswersTheLayout() {
        Home home = session.Player.Value.Home;
        home.SetArea(11);
        home.SetHeight(6);

        Save(2, "first");

        HomeLayout layout = home.Layouts.Single();
        Assert.That((layout.Id, layout.Name, layout.Area, layout.Height), Is.EqualTo((2, "first", (byte) 11, (byte) 6)));
        Assert.That(Sent(), Does.Contain(Convert.ToHexString(CubePacket.SaveLayout(session.AccountId, layout).ToArray())));
    }

    [Test]
    public void Save_ToTheSameSlotAgain_ReplacesTheLayout() {
        Save(2, "first");
        Save(2, "second");

        Assert.That(session.Player.Value.Home.Layouts.Select(layout => layout.Name), Is.EqualTo(new[] { "second" }));
    }

    [Test]
    public void Load_WhileTheIndoorHoldsCubes_IsRefusedWithANotice() {
        Save(1, "layout");
        plot.Cubes[new Vector3B(0, 0, 1)] = new PlotCube(TestGameSession.Metadata(50400001)) { Type = PlotCube.CubeType.Default };
        test.TakeSent();

        Load(1);

        Assert.That(Sent(), Is.EqualTo(new[] {
            Convert.ToHexString(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert).ToArray()),
        }));
    }

    [Test]
    public void Load_OfAnEmptySlot_AnswersTheDatabaseError() {
        Load(3);

        Assert.That(Sent(), Is.EqualTo(new[] { Convert.ToHexString(CubePacket.Error(UgcMapError.s_ugcmap_db).ToArray()) }));
    }

    [Test]
    public void Load_SetsTheHomeToTheLayoutsSize() {
        Home home = session.Player.Value.Home;
        home.SetArea(12);
        home.SetHeight(7);
        Save(1, "layout");
        home.SetArea(8);
        home.SetHeight(4);

        Load(1);

        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 12, (byte) 7)));
    }
}
