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
    private const int Cube = 50200911, OtherCube = 50200912, House = 50200913, TemplateCube = 50200914;
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
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        ItemMetadata house = Furnishing(House);
        test.Register(Furnishing(Cube), Furnishing(OtherCube), house with { Property = house.Property with { Type = 6, SubType = 4 } },
            Furnishing(TemplateCube) with { Mesh = "ugc_mesh" });
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        session.Currency.Meso = 5000;
        test.TakeSent();
        return (session, plot);
    }

    private void Place(GameSession session, int held, int placed) {
        session.HeldCube = null;
        if (held != 0) {
            session.ItemMetadata.TryGet(held, out ItemMetadata? metadata);
            session.HeldCube = new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction };
        }
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.Write<Vector3B>(new Vector3B(-1, -1, 1));
            writer.WriteInt(placed);
            writer.WriteLong(0);
            writer.WriteLong(0);
            writer.WriteBool(false);
            writer.WriteFloat(0);
        }));
    }

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

    [Test]
    public void TheHeldItem_IsPlaced() {
        (GameSession session, Plot plot) = StartInHome();
        Place(session, Cube, Cube);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(4000));
    }
}
