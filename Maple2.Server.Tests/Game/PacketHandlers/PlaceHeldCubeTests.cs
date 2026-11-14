using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 10 places only the cube the player holds (the client places its held cube, cubecraft_2.cpp:936-960):
// a place with nothing held, or of another item than the one held, is refused with 0x0A s_ugcmap_null_cube_item_info
// and places and takes nothing. The item ids are made up.
public class PlaceHeldCubeTests {
    private const int Cube = 50200911, OtherCube = 50200912, House = 50200913, TemplateCube = 50200914, TrophyCube = 50200915;
    private const int QuestCube = 50400341; // the install_item code of KR 80000631
    private const int Trophy = 23100098; // made up
    private TestGameSession test = null!;

    private static ItemMetadata Furnishing(int id) => TestGameSession.Metadata(id) with {
        Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
        Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
    };

    private (GameSession Session, Plot Plot) StartInHome() {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry> {
            [Cube] = new(Cube, true, FurnishingCurrencyType.Meso, 1000),
            [OtherCube] = new(OtherCube, true, FurnishingCurrencyType.Meso, 1000),
            [House] = new(House, true, FurnishingCurrencyType.Meso, 1000),
            [TemplateCube] = new(TemplateCube, true, FurnishingCurrencyType.Meso, 1000),
            [TrophyCube] = new(TrophyCube, true, FurnishingCurrencyType.Meso, 1000),
            [QuestCube] = new(QuestCube, true, FurnishingCurrencyType.Meso, 1000),
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        ItemMetadata house = Furnishing(House);
        test.Register(Furnishing(Cube), Furnishing(OtherCube), house with { Property = house.Property with { Type = 6, SubType = 4 } },
            Furnishing(TemplateCube) with { Mesh = "ugc_mesh" }, Furnishing(QuestCube),
            Furnishing(TrophyCube) with { Housing = new ItemMetadataHousing(Trophy, 1, 0, HousingCategory.None, false) });
        test.AddAchievement(TestGameSession.Trophy(Trophy, ConditionType.home_goto, [1, 2]));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        session.Currency.Meso = 5000;
        test.TakeSent();
        return (session, plot);
    }

    private void Place(GameSession session, int held, int placed, Vector3B? position = null, long uid = 0, float rotation = 0, byte command = 10) {
        session.HeldCube = null;
        if (held != 0) {
            session.ItemMetadata.TryGet(held, out ItemMetadata? metadata);
            session.HeldCube = new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction };
        }
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            writer.Write<Vector3B>(position ?? new Vector3B(-1, -1, 1));
            writer.WriteInt(placed);
            writer.WriteLong(uid);
            writer.WriteLong(0);
            writer.WriteBool(false);
            writer.WriteFloat(rotation);
            if (command == 16) {
                writer.WriteByte(0); // the side the cube is attached to
            }
        }));
    }

    // RequestCube 15 or 16 at the cell of a placed Cube.
    private void Replace(GameSession session, int held, int replaced, byte command = 15) => Place(session, held, replaced, command: command);

    private void AssertRefused(GameSession session, Plot plot) {
        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_null_cube_item_info, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [Test]
    public void NothingHeld_IsRefused() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, 0, Cube);
        AssertRefused(session, plot);
    }

    [Test]
    public void AnotherItemHeld_IsRefused() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, OtherCube, Cube);
        AssertRefused(session, plot);
    }

    // The home's group allows no house (installableBuildingCount 0): 0x0A s_ugcmap_building_count (45), nothing paid.
    [Test]
    public void AHouseAtHome_IsRefusedByTheBuildingLimit() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, House, House);
        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_building_count, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    // A cell above the home's build height gets 0x0A s_ugcmap_height_limit (43), one outside its area
    // s_ugcmap_area_limit (44); nothing is paid. Both were 44.
    [Test]
    public void OutsideTheBuildSpace_IsRefusedByHeightOrArea() {
        (GameSession session, Plot plot) = StartInHome();
        var above = new Vector3B(-1, -1, session.Player.Value.Home.Height + 1);
        Place(session, Cube, Cube, above);
        byte[] height = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_height_limit, session.Player.ObjectId).ToArray();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(height) }));

        Place(session, Cube, Cube, new Vector3B(1, -1, 1));
        byte[] area = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_area_limit, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(area) }));
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    // A template cube placed without a template look: 0x0A s_err_ugcmap_cant_build_empty_ugc (98), nothing paid.
    [Test]
    public void ATemplateCubeWithoutALook_IsRefused() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, TemplateCube, TemplateCube);
        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_err_ugcmap_cant_build_empty_ugc, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    // A cube that needs a trophy grade the builder has not cleared is locked (cubecraft_2.cpp:954, mypc.cpp:4446):
    // 0x0A s_ugcmap_cube_lock (88), nothing paid. Once the grade is cleared it is placed.
    [Test]
    public void ATrophyCube_IsLockedUntilTheGradeIsCleared() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, TrophyCube, TrophyCube);
        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_cube_lock, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });

        session.ConditionUpdate(ConditionType.home_goto);
        Assert.That(session.Achievement.HasAchievement(Trophy, 1), Is.True);
        Place(session, TrophyCube, TrophyCube);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(4000));
    }

    // A layout or package apply places a locked cube too, as the client checks the lock only on a player's place.
    [Test]
    public void ATrophyCubeFromALayout_IsNotLocked() {
        (GameSession session, Plot plot) = StartInHome();
        session.ItemMetadata.TryGet(TrophyCube, out ItemMetadata? metadata);
        Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!) { Type = PlotCube.CubeType.Default }, plot, metadata!, new Vector3B(-1, -1, 1), 0, out _), Is.True);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
    }

    // A layout or package apply (not a player's place) keeps no group limit, as the client checks none: a house is
    // placed at home past its building limit of 0.
    [Test]
    public void AHouseFromALayout_KeepsNoBuildingLimit() {
        (GameSession session, Plot plot) = StartInHome();
        session.ItemMetadata.TryGet(House, out ItemMetadata? metadata);
        Assert.That(session.Housing.TryPlaceCube(new PlotCube(metadata!) { Type = PlotCube.CubeType.Default }, plot, metadata!, new Vector3B(-1, -1, 1), 0, out _), Is.True);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
    }

    // The storage uid the client sends is a stack of another item: the stack is not spent and the named cube is bought
    // as any cube not in storage. It was placed from the cheap stack, and taking it back gave the other item.
    [Test]
    public void AStackOfAnotherItem_IsNotSpent() {
        (GameSession session, Plot plot) = StartInHome();
        long uid = session.Item.Furnishing.AddCube(Cube);
        Assert.That(uid, Is.Not.Zero);

        Place(session, OtherCube, OtherCube, uid: uid);
        Assert.Multiple(() => {
            Assert.That(plot.Cubes.Values.Select(cube => cube.ItemId), Is.EqualTo(new[] { OtherCube }));
            Assert.That(session.Item.Furnishing.GetCube(uid)?.Amount, Is.EqualTo(1));
            Assert.That(session.Currency.Meso, Is.EqualTo(4000));
        });
    }

    // 80000631: <condition type="install_item" code="50400341" value="1" />, a place of the cube counts; a refused
    // place of it (nothing held) or a place of another cube does not.
    [Test]
    public void InstallItem_80000631_ThePlacedCube() {
        (GameSession session, _) = StartInHome();
        Quest quest = test.StartQuest(TestGameSession.QuestData(91000001) with {
            Conditions = [new ConditionMetadata(ConditionType.install_item, 1, new ConditionMetadata.Parameters(Integers: [QuestCube]), null)],
        });

        Place(session, 0, QuestCube);
        Place(session, Cube, Cube);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Place(session, QuestCube, QuestCube, new Vector3B(-2, -1, 1));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
    }

    // RequestCube 15 and 16 replace a cell with the held cube too (cubecraft_2.cpp:1007-1033, :1299-1318): a replace
    // with nothing held, or of another item than the one held, is refused with 0x0F s_ugcmap_null_cube_item_info and
    // leaves the placed cube. It replaced the cube with any furnishing.
    [TestCase(0, (byte) 15, TestName = "Replace_NothingHeld_IsRefused")]
    [TestCase(Cube, (byte) 15, TestName = "Replace_AnotherItemHeld_IsRefused")]
    [TestCase(0, (byte) 16, TestName = "ReplaceAttached_NothingHeld_IsRefused")]
    public void Replace_WithoutTheHeldItem_IsRefused(int held, byte command) {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, Cube, Cube);
        test.TakeSent();

        Replace(session, held, OtherCube, command);

        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Replace, UgcMapError.s_ugcmap_null_cube_item_info, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes.Values.Select(cube => cube.ItemId), Is.EqualTo(new[] { Cube }));
            Assert.That(session.Currency.Meso, Is.EqualTo(4000));
        });
    }

    [Test]
    public void Replace_TheHeldItem_Replaces() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, Cube, Cube);

        Replace(session, OtherCube, OtherCube);

        Assert.That(plot.Cubes.Values.Select(cube => cube.ItemId), Is.EqualTo(new[] { OtherCube }));
    }

    // The client sends a whole angle (cubecraft_2.cpp:991-995): a NaN or infinite one is refused with
    // s_ugcmap_null_cube_item_info and nothing is placed or paid. It was stored with the cube.
    [TestCase(float.NaN, TestName = "Place_NaNRotation_IsRefused")]
    [TestCase(float.PositiveInfinity, TestName = "Place_InfiniteRotation_IsRefused")]
    public void Place_NonFiniteRotation_IsRefused(float rotation) {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, Cube, Cube, rotation: rotation);
        AssertRefused(session, plot);
    }

    [Test]
    public void TheHeldItem_IsPlaced() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, Cube, Cube);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(4000));
    }
}
