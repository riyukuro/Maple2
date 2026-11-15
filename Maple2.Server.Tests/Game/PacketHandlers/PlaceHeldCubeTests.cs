using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// RequestCube 10 places only the cube the player holds (the client places its held cube, cubecraft_2.cpp:936-960):
// a place with nothing held, or of another item than the one held, is refused with 0x0A s_ugcmap_null_cube_item_info
// and places and takes nothing. The item ids are made up.
public class PlaceHeldCubeTests {
    private const int Cube = 50200911, OtherCube = 50200912, House = 50200913, TemplateCube = 50200914, TrophyCube = 50200915, CategoryCube = 50200916;
    private const int QuestCube = 50400341; // the install_item code of KR 80000631
    private const int RedCushion = 50200108; // the install_item code of KR 90000661
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
            [RedCushion] = new(RedCushion, true, FurnishingCurrencyType.Meso, 1000),
            [CategoryCube] = new(CategoryCube, true, FurnishingCurrencyType.Meso, 1000),
        }));
        // No construct row: a place gives no decorating exp unless a test sets one (ConstructExpTests below).
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry>()));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        ItemMetadata house = Furnishing(House);
        test.Register(Furnishing(Cube), Furnishing(OtherCube), house with { Property = house.Property with { Type = 6, SubType = 4 } },
            Furnishing(TemplateCube) with { Mesh = "ugc_mesh" }, Furnishing(QuestCube), Furnishing(RedCushion),
            Furnishing(TrophyCube) with { Housing = new ItemMetadataHousing(Trophy, 1, 0, HousingCategory.None, false) },
            Furnishing(CategoryCube) with { Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false) { CategoryIndex = 3 } });
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

    // HousingLevelhurdle on: a cube whose category needs a housing level above the builder's is locked until the home
    // reaches it; off, the category is not read.
    [TestCase(true)]
    [TestCase(false)]
    public void ACategoryCube_IsLockedBelowTheCategoryLevel(bool hurdle) {
        (GameSession session, Plot plot) = StartInHome();
        test.SetTable(new UgcCubeCategoryTable(new Dictionary<int, int> { [3] = 2 }));
        if (!hurdle) {
            test.SetFeaturesOff(CubeEditRules.LevelHurdleFeature);
            Place(session, CategoryCube, CategoryCube);
            Assert.That(plot.Cubes, Has.Count.EqualTo(1));
            return;
        }
        Place(session, CategoryCube, CategoryCube);
        byte[] refusal = CubePacket.EditError(CubePacket.Edit.Place, UgcMapError.s_ugcmap_cube_lock, session.Player.ObjectId).ToArray();
        Assert.Multiple(() => {
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(refusal) }));
            Assert.That(plot.Cubes, Is.Empty);
        });

        session.Player.Value.Home.DecorationLevel = 2;
        Place(session, CategoryCube, CategoryCube);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
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

    private long StoredMeso(GameSession session) {
        using var context = new Ms2Context(test.Options);
        return context.Character.AsNoTracking().Single(row => row.Id == session.CharacterId).Currency.Meso;
    }

    // A bought place stores the price with it: the cube's row was written at once and the mesos taken in memory only,
    // so a crash before the next save kept the placed cube and gave the price back. A refused place stores nothing.
    [Test]
    public void ABoughtPlace_StoresThePrice() {
        (GameSession session, Plot plot) = StartInHome();
        long before = StoredMeso(session);

        Place(session, 0, Cube);
        Assert.That(StoredMeso(session), Is.EqualTo(before));

        Place(session, Cube, Cube);
        Assert.Multiple(() => {
            Assert.That(plot.Cubes.Values.Select(cube => cube.ItemId), Is.EqualTo(new[] { Cube }));
            Assert.That((session.Currency.Meso, StoredMeso(session)), Is.EqualTo((4000L, 4000L)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    private long StoredBuildMeso(GameSession session) {
        using var context = new Ms2Context(test.Options);
        return context.Home.AsNoTracking().Single(row => row.AccountId == session.AccountId).BuildMeso;
    }

    // A build helper's cube is bought from the home's budget into the owner's storage. The storage row was written at
    // once and the charge kept in memory only, so a crash before the next save kept the item and gave the budget back.
    private (GameSession Owner, GameSession Helper, Plot Plot) HelperInHome() {
        (GameSession owner, Plot plot) = StartInHome();
        owner.Player.Value.Home.BuildMeso = 3000;
        Assert.That(owner.Housing.SaveHome(), Is.True);
        var helperTest = new TestGameSession(test.Options);
        GameSession helper = helperTest.Start();
        return (owner, helper, plot);
    }

    private static bool HelperPlace(GameSession owner, GameSession helper, Plot plot) {
        owner.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);
        return owner.Housing.TryPlaceCube(new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction }, plot, metadata!, new Vector3B(-1, -1, 1), 0, out _, helper: helper);
    }

    [Test]
    public void AHelpersBuy_StoresTheBudgetCharge() {
        (GameSession owner, GameSession helper, Plot plot) = HelperInHome();

        Assert.That(HelperPlace(owner, helper, plot), Is.True);
        Assert.Multiple(() => {
            Assert.That(plot.Cubes.Values.Select(cube => cube.ItemId), Is.EqualTo(new[] { Cube }));
            Assert.That((owner.Player.Value.Home.BuildMeso, StoredBuildMeso(owner)), Is.EqualTo((2000L, 2000L)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The charge not stored: nothing is bought, the budget is given back and the helper told s_ugcmap_db.
    [Test]
    public void AHelpersBuy_ChargeNotStored_BuysNothing() {
        (GameSession owner, GameSession helper, Plot plot) = HelperInHome();
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "Home");

        Assert.That(HelperPlace(owner, helper, plot), Is.False);
        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(owner.Item.Furnishing.GetItem(Cube), Is.Null);
            Assert.That((owner.Player.Value.Home.BuildMeso, StoredBuildMeso(owner)), Is.EqualTo((3000L, 3000L)));
        });
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

    // KR 90000661: <condition type="install_item" code="50200108" value="1" />. The client sends a replace (15) for the
    // same confirm as a place when the cell holds another cube (cubecraft_2.cpp:1007-1033); the replace counts as the
    // place. A refused replace does not.
    [Test]
    public void InstallItem_90000661_AReplaceCounts() {
        (GameSession session, _) = StartInHome();
        Quest quest = test.StartQuest(TestGameSession.QuestData(90000661) with {
            Conditions = [new ConditionMetadata(ConditionType.install_item, 1, new ConditionMetadata.Parameters(Integers: [RedCushion]), null)],
        });
        Place(session, Cube, Cube);

        Replace(session, 0, RedCushion);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(0));
        Replace(session, RedCushion, RedCushion);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(quest.Conditions[0].Counter, Is.EqualTo(1));
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

    // Decorating exp (the ConstructExp feature, on in the test session): the construct row's curve at the decorator's
    // level for each placed cube, up to ConstructExpMaxCount a day; the first place past it gets ResponseCube 0x3d [100][1], later ones nothing.
    private void WithConstructExp(GameSession session, int maxPerDay) {
        test.SetTable(new CommonExpTable(new Dictionary<ExpType, CommonExpTable.Entry> { [ExpType.construct] = new(1, 1f) }));
        test.SetTable(new ExpTable(new Dictionary<int, IReadOnlyDictionary<int, long>> {
            [1] = Enumerable.Range(1, 99).ToDictionary(level => level, level => level * 10L),
        }, Enumerable.Range(1, 99).ToDictionary(level => level, _ => 1_000_000L)));
        test.SetServerTable(new PrestigeExpTable(new Dictionary<ExpType, long>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { ConstructExpMaxCount = maxPerDay });
        session.Player.Value.Character.Level = 50;
    }

    private static readonly string OverTime = Convert.ToHexString(CubePacket.ConstructExpOverTime().ToArray());

    [Test]
    public void APlace_PaysTheConstructExpUpToTheDailyCount() {
        (GameSession session, Plot plot) = StartInHome();
        WithConstructExp(session, maxPerDay: 1);
        long exp = session.Player.Value.Character.Exp;

        Place(session, Cube, Cube, new Vector3B(-1, -1, 1));
        List<string> first = test.TakeSent().Select(Convert.ToHexString).ToList();
        long paid = session.Player.Value.Character.Exp - exp;
        Place(session, Cube, Cube, new Vector3B(-2, -1, 1));
        List<string> second = test.TakeSent().Select(Convert.ToHexString).ToList();
        Place(session, Cube, Cube, new Vector3B(-3, -1, 1));
        List<string> third = test.TakeSent().Select(Convert.ToHexString).ToList();

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Has.Count.EqualTo(3));
            Assert.That(paid, Is.EqualTo(500));
            Assert.That(session.Player.Value.Character.Exp - exp, Is.EqualTo(500));
            Assert.That(first, Has.None.EqualTo(OverTime));
            Assert.That(second.Count(packet => packet == OverTime), Is.EqualTo(1));
            Assert.That(third, Has.None.EqualTo(OverTime));
            Assert.That(session.Player.Value.Character.ConstructExpCount, Is.EqualTo(2));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    [Test]
    public void APlannerPlace_PaysNoConstructExp() {
        (GameSession session, Plot plot) = StartInHome();
        WithConstructExp(session, maxPerDay: 1);
        plot.PlotMode = PlotMode.DecorPlanner;
        long exp = session.Player.Value.Character.Exp;

        Place(session, Cube, Cube);

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Has.Count.EqualTo(1));
            Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(exp));
            Assert.That(session.Player.Value.Character.ConstructExpCount, Is.EqualTo(0));
        });
    }

    // The operator's feature.xml has ConstructExp off: the construct row is still in the table, but a place pays
    // nothing, counts nothing and sends no notice.
    [Test]
    public void WithTheFeatureOff_APlacePaysNoConstructExp() {
        (GameSession session, Plot plot) = StartInHome();
        WithConstructExp(session, maxPerDay: 1);
        test.SetFeaturesOff("ConstructExp");
        long exp = session.Player.Value.Character.Exp;

        Place(session, Cube, Cube, new Vector3B(-1, -1, 1));
        Place(session, Cube, Cube, new Vector3B(-2, -1, 1));
        List<string> sent = test.TakeSent().Select(Convert.ToHexString).ToList();

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Has.Count.EqualTo(2));
            Assert.That(session.Player.Value.Character.Exp, Is.EqualTo(exp));
            Assert.That(session.Player.Value.Character.ConstructExpCount, Is.EqualTo(0));
            Assert.That(sent, Has.None.EqualTo(OverTime));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // ResponseCube 0x3d: the error byte 100, then the sub byte the client tests before it shows the notice.
    [Test]
    public void TheOverTimeNotice_IsResponseCube0x3dWithError100()
        => Assert.That(CubePacket.ConstructExpOverTime().ToArray()[2..], Is.EqualTo(new byte[] { 0x3d, 100, 1 }));

    // What a stop would leave: the stored amount of the cube's storage rows and its plot rows.
    private (int Stored, int Placed) StoredCube(int itemId = Cube) {
        using var context = new Ms2Context(test.Options);
        int stored = context.Item.AsNoTracking().Where(row => row.ItemId == itemId && row.OwnerId == test.AccountId).Sum(row => row.Amount);
        int placed = context.UgcMapCube.AsNoTracking().Count(row => row.ItemId == itemId);
        return (stored, placed);
    }

    // A place from a storage stack stores the stack's lower count with the cube's row. Before, the cube's row was
    // written at once and the stack waited for the session's next save: a stop before it kept the cube twice.
    [Test]
    public void APlaceFromStorage_StoresTheStackWithTheCube() {
        (GameSession session, Plot plot) = StartInHome();
        long uid = session.Item.Furnishing.AddCube(Cube);
        session.Item.Furnishing.AddCube(Cube);
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Furnishing.Save(db);
        }
        Assume.That(StoredCube(), Is.EqualTo((2, 0)));

        Place(session, Cube, Cube, uid: uid);

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Has.Count.EqualTo(1));
            Assert.That(session.Item.Furnishing.GetCube(uid)?.Amount, Is.EqualTo(1));
            Assert.That(StoredCube(), Is.EqualTo((1, 1)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A bought cube's new storage row is stored as the place leaves it (no piece in storage) with the cube's row.
    // Before, the row was made with one piece and the place's take waited for the next save: a bought cube twice.
    [Test]
    public void ABoughtPlace_StoresNoStoragePiece() {
        (GameSession session, Plot plot) = StartInHome();

        Place(session, Cube, Cube);

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Has.Count.EqualTo(1));
            Assert.That(StoredCube(), Is.EqualTo((0, 1)));
            Assert.That((session.Currency.Meso, StoredMeso(session)), Is.EqualTo((4000L, 4000L)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A pickup stores the stack's higher count with the cube row's delete. Before, the row went at once and the stack
    // waited for the next save: a stop before it lost the cube.
    [Test]
    public void APickup_StoresTheStackWithTheDelete() {
        (GameSession session, Plot plot) = StartInHome();
        long uid = session.Item.Furnishing.AddCube(Cube);
        Place(session, Cube, Cube, uid: uid);
        Vector3B position = plot.Cubes.Keys.Single();
        using (GameStorage.Request db = test.Storage.Context()) {
            session.Item.Furnishing.Save(db);
        }
        Assume.That(StoredCube(), Is.EqualTo((0, 1)));

        Assert.That(session.Housing.TryRemoveCube(plot, position, out _), Is.True);

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Item.Furnishing.GetCube(uid)?.Amount, Is.EqualTo(1));
            Assert.That(StoredCube(), Is.EqualTo((1, 0)));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A bought place whose rows are not stored places nothing and takes nothing: the price is given back and storage
    // keeps no piece.
    [Test]
    public void ABoughtPlace_NotStored_TakesNothing() {
        (GameSession session, Plot plot) = StartInHome();
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Item>().Any();

        Place(session, Cube, Cube);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(session.Item.Furnishing.GetItem(Cube), Is.Null.Or.Property(nameof(Item.Amount)).EqualTo(0));
        });
    }

    // A pickup whose rows are not stored leaves the cube where it was: on the plot, and storage keeps its count. (The
    // in-memory test store has no rollback, so only memory is checked.)
    [Test]
    public void APickup_NotStored_LeavesTheCube() {
        (GameSession session, Plot plot) = StartInHome();
        long uid = session.Item.Furnishing.AddCube(Cube);
        Place(session, Cube, Cube, uid: uid);
        Vector3B position = plot.Cubes.Keys.Single();
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.UgcMapCube>().Any(entry => entry.State == EntityState.Deleted);

        Assert.That(session.Housing.TryRemoveCube(plot, position, out _), Is.False);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { position }));
            Assert.That(session.Item.Furnishing.GetCube(uid)?.Amount, Is.EqualTo(0));
        });
    }

    // A pickup of a cube storage cannot take back (here one not held in the furnishing inventory) leaves it on the
    // plot. Before, it was taken off the plot and went nowhere.
    [Test]
    public void APickup_StorageTakesNothing_LeavesTheCube() {
        (GameSession session, Plot plot) = StartInHome();
        session.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);
        var position = new Vector3B(-1, -1, 1);
        plot.Cubes[position] = new PlotCube(metadata!) { Position = position, Type = PlotCube.CubeType.Construction };

        Assert.That(session.Housing.TryRemoveCube(plot, position, out _), Is.False);

        Assert.That(plot.Cubes.Keys, Is.EqualTo(new[] { position }));
    }

    // A build helper's cube, bought from the owner's budget, stores the budget (the home's row), the storage row and
    // the cube's row together. Before, the budget waited for the next save of the home and the stack for the session's.
    // The owner stands in as the helper here: the budget path is the helper's.
    [Test]
    public void ABudgetPlace_StoresTheBudgetWithTheCube() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.BuildMeso = 3000;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assume.That(db.SaveHome(session.Player.Value.Home), Is.True);
        }
        session.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);

        bool placed = session.Housing.TryPlaceCube(new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction }, plot, metadata!,
            new Vector3B(-1, -1, 1), 0, out _, helper: session, byPlayer: true);

        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(placed, Is.True);
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(2000));
            Assert.That(context.Home.AsNoTracking().Single(row => row.AccountId == test.AccountId).BuildMeso, Is.EqualTo(2000));
            Assert.That(StoredCube(), Is.EqualTo((0, 1)));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }

    private long StoredBuildMeso() {
        using var context = new Ms2Context(test.Options);
        return context.Home.AsNoTracking().Single(row => row.AccountId == test.AccountId).BuildMeso;
    }

    // RequestCube 56: a raise of the build budget stores the mesos it takes with the home's budget, and a fall the
    // mesos it gives. Before, the home's row was stored at once and the wallet left to the next session save.
    [Test]
    public void ABudgetChange_StoresTheWalletWithTheHome() {
        (GameSession session, _) = StartInHome();
        using (GameStorage.Request db = test.Storage.Context()) {
            db.SaveCurrency(session.Player);
        }

        session.Housing.SetBuildBudget(3000, 0);
        Assert.Multiple(() => {
            Assert.That((session.Currency.Meso, StoredMeso(session)), Is.EqualTo((2000L, 2000L)));
            Assert.That(StoredBuildMeso(), Is.EqualTo(3000));
        });

        session.Housing.SetBuildBudget(1000, 0);
        Assert.Multiple(() => {
            Assert.That((session.Currency.Meso, StoredMeso(session)), Is.EqualTo((4000L, 4000L)));
            Assert.That(StoredBuildMeso(), Is.EqualTo(1000));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A budget change whose store fails changes nothing: the mesos are given back and the budget stays.
    [Test]
    public void ABudgetChange_NotStored_ChangesNothing() {
        (GameSession session, _) = StartInHome();
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Home>().Any();

        session.Housing.SetBuildBudget(3000, 0);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(0));
        });
    }

    // A raise whose store fails after a helper spent part of it meanwhile: the budget goes back by the raise (what is
    // left of it) and only that much comes back to the wallet. Before, the budget was put back only when unchanged,
    // so it kept the raise's rest and the wallet got the whole raise back.
    [Test]
    public void ABudgetRaise_NotStored_AfterASpend_GivesBackOnlyTheRest() {
        (GameSession session, _) = StartInHome();
        Home home = session.Player.Value.Home;
        test.FailSave = context => {
            if (!context.ChangeTracker.Entries<Maple2.Database.Model.Home>().Any()) {
                return false;
            }
            home.BuildMeso -= 1000; // a helper's place between the raise and its store
            return true;
        };

        session.Housing.SetBuildBudget(3000, 0);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(home.BuildMeso, Is.EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(4000));
        });
    }

    // A place from storage whose rows are not stored places nothing and keeps the stack's count.
    [Test]
    public void APlaceFromStorage_NotStored_KeepsTheStack() {
        (GameSession session, Plot plot) = StartInHome();
        long uid = session.Item.Furnishing.AddCube(Cube);
        session.Item.Furnishing.AddCube(Cube);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Item>().Any();

        Place(session, Cube, Cube, uid: uid);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Item.Furnishing.GetCube(uid)?.Amount, Is.EqualTo(2));
        });
    }

    // A helper's budget place whose rows are not stored places nothing: the budget is given back and storage keeps
    // no piece.
    [Test]
    public void ABudgetPlace_NotStored_GivesTheBudgetBack() {
        (GameSession session, Plot plot) = StartInHome();
        session.Player.Value.Home.BuildMeso = 3000;
        session.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Home>().Any();

        bool placed = session.Housing.TryPlaceCube(new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction }, plot, metadata!,
            new Vector3B(-1, -1, 1), 0, out _, helper: session, byPlayer: true);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That(placed, Is.False);
            Assert.That(plot.Cubes, Is.Empty);
            Assert.That(session.Player.Value.Home.BuildMeso, Is.EqualTo(3000));
            Assert.That(session.Item.Furnishing.GetItem(Cube), Is.Null.Or.Property(nameof(Item.Amount)).EqualTo(0));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000));
        });
    }
}
