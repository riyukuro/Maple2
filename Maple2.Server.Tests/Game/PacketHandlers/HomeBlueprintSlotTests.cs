using Maple2.Database.Storage;
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

// RequestCube 64 (save the home to a blueprint slot) and 65 (load a slot): as the layout slots, with the planner's size
// kept in the slot and the look (background, lighting, camera) kept and applied by a load; a slot out of range is not
// saved, a load is refused while the indoor holds cubes and for an empty slot.
public class HomeBlueprintSlotTests {
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

    private void Save(int slot, string name) => Send(64, writer => { writer.WriteInt(slot); writer.WriteUnicodeString(name); });
    private void Load(int slot) => Send(65, writer => writer.WriteInt(slot));

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

        Assert.That(session.Player.Value.Home.Blueprints, Is.Empty);
        Assert.That(test.TakeSent(), Is.Empty);
    }

    [Test]
    public void Save_KeepsTheHomesSize_AndAnswersTheLayout() {
        Home home = session.Player.Value.Home;
        home.SetPlannerArea(11);
        home.SetPlannerHeight(6);

        Save(2, "first");

        HomeLayout layout = home.Blueprints.Single();
        Assert.That((layout.Id, layout.Name, layout.Area, layout.Height), Is.EqualTo((2, "first", (byte) 11, (byte) 6)));
        Assert.That(Sent(), Does.Contain(Convert.ToHexString(CubePacket.SaveBlueprint(session.AccountId, layout).ToArray())));
    }

    [Test]
    public void Save_ToTheSameSlotAgain_ReplacesTheLayout() {
        Save(2, "first");
        Save(2, "second");

        Assert.That(session.Player.Value.Home.Blueprints.Select(layout => layout.Name), Is.EqualTo(new[] { "second" }));
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
        home.SetPlannerArea(12);
        home.SetPlannerHeight(7);
        home.SetLighting(HomeLighting.Warm);
        Save(1, "layout");
        home.SetArea(8);
        home.SetHeight(4);
        home.SetLighting(HomeLighting.Cool);

        Load(1);

        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 12, (byte) 7)));
        Assert.That(home.Lighting, Is.EqualTo(HomeLighting.Warm));
    }

    // The look the slot was saved with is stored and read back with the layout (a layout row read had the default look).
    [Test]
    public void SavedLook_IsReadBackFromTheStore() {
        Home home = session.Player.Value.Home;
        home.SetBackground(HomeBackground.MoonlitForest);
        home.SetLighting(HomeLighting.Warm);
        home.SetCamera(HomeCamera.TopView);
        Save(1, "layout");

        HomeLayout saved = home.Blueprints.Single();
        using GameStorage.Request db = test.Storage.Context();
        HomeLayout read = db.GetHomeLayout(saved.Uid)!;

        Assert.That((saved.Background, saved.Lighting, saved.Camera), Is.EqualTo((HomeBackground.MoonlitForest, HomeLighting.Warm, HomeCamera.TopView)));
        Assert.That((read.Background, read.Lighting, read.Camera), Is.EqualTo((HomeBackground.MoonlitForest, HomeLighting.Warm, HomeCamera.TopView)));
    }
}
