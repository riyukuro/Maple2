using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 70 confirms the bill of a blueprint item: only the item that staged the blueprint is accepted. The
// indoor must hold no cubes (a notice), a staged blueprint whose layout is gone answers the database error, and the
// applied layout sets the home's size and look and clears the staging.
public class HomeApplyBlueprintTests {
    private const long ItemUid = 7001;
    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Plot plot = null!;
    private ItemBlueprint staged = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.Start();
        session.Housing.InitNewHome("home", null);
        plot = test.EnterHome();
        session.Field!.Players.Clear();

        HomeLayout layout;
        using (GameStorage.Request db = test.Storage.Context()) {
            layout = db.SaveHomeLayout(new HomeLayout(0, "bp", 12, 7, DateTimeOffset.UtcNow, new List<PlotCube>()) {
                Background = HomeBackground.MoonlitForest,
                Lighting = HomeLighting.Warm,
                Camera = HomeCamera.TopView,
            })!;
        }
        staged = new ItemBlueprint { BlueprintUid = layout.Uid };
        session.Housing.StagedItemBlueprint = staged;
        session.Housing.StagedBlueprintItemUid = ItemUid;
        test.TakeSent();
    }

    private string[] Apply(long itemUid) {
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(70);
            writer.WriteLong(itemUid);
        }));
        return test.TakeSent().Select(Convert.ToHexString).ToArray();
    }

    [Test]
    public void TheStagedItem_AppliesSizeAndLook_AndClearsTheStaging() {
        Home home = session.Player.Value.Home;

        Apply(ItemUid);

        Assert.That((home.Area, home.Height), Is.EqualTo(((byte) 12, (byte) 7)));
        Assert.That((home.Background, home.Lighting, home.Camera), Is.EqualTo((HomeBackground.MoonlitForest, HomeLighting.Warm, HomeCamera.TopView)));
        Assert.That(session.Housing.StagedItemBlueprint, Is.Null);
        Assert.That(session.Housing.StagedBlueprintItemUid, Is.EqualTo(0));
    }

    [Test]
    public void AnotherItem_AppliesNothing_AndKeepsTheStaging() {
        Home home = session.Player.Value.Home;
        byte area = home.Area;

        Apply(ItemUid + 1);

        Assert.That(home.Area, Is.EqualTo(area));
        Assert.That(session.Housing.StagedItemBlueprint, Is.SameAs(staged));
    }

    [Test]
    public void WithCubesOnThePlot_IsRefusedWithANotice() {
        plot.Cubes[new Vector3B(0, 0, 1)] = new PlotCube(TestGameSession.Metadata(50400001)) { Type = PlotCube.CubeType.Default };

        Assert.That(Apply(ItemUid), Is.EqualTo(new[] {
            Convert.ToHexString(NoticePacket.Message(StringCode.s_err_ugcmap_package_clear_indoor_first, NoticePacket.Flags.Message | NoticePacket.Flags.Alert).ToArray()),
        }));
        Assert.That(session.Housing.StagedItemBlueprint, Is.SameAs(staged));
    }

    [Test]
    public void WhoseLayoutIsGone_AnswersTheDatabaseError() {
        staged.BlueprintUid = 987654321;

        Assert.That(Apply(ItemUid), Is.EqualTo(new[] { Convert.ToHexString(CubePacket.Error(UgcMapError.s_ugcmap_db).ToArray()) }));
    }

    [Test]
    public void WithNothingStaged_IsIgnored() {
        session.Housing.StagedItemBlueprint = null;

        Assert.That(Apply(ItemUid), Is.Empty);
    }
}
