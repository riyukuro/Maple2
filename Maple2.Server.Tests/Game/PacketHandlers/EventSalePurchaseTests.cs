using System;
using System.Collections.Generic;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The cube price sale and the design price sale in a purchase, driven through the handlers on a TestGameSession:
// the sale price is what is taken, once. The item and event ids are made up.
public class EventSalePurchaseTests {
    private const int Cube = 50200901;
    private const int Design = 11300901;
    private const int Town = 90000100;
    private const short Filter = 100;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    #region SaleHousing
    // A home whose build palette sells the cube for 1000 of the currency; the cube's housing filter is 100.
    private (GameSession Session, Plot Plot) StartInHome(FurnishingCurrencyType currency) {
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry> {
            [Cube] = new(Cube, true, currency, 1000),
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.Register(TestGameSession.Metadata(Cube) with {
            Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false, Filter: Filter),
            Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
        });
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        Plot plot = test.EnterHome();
        session.Currency.Meso = 5000;
        session.Currency.Meret = 5000;
        test.TakeSent();
        return (session, plot);
    }

    // RequestCube 10: the position, the held cube (item id, uid, expiry, no template) and the rotation.
    private void Place(GameSession session, sbyte x, sbyte y, sbyte z) {
        session.ItemMetadata.TryGet(Cube, out ItemMetadata? metadata);
        session.HeldCube = new PlotCube(metadata!) { Type = PlotCube.CubeType.Construction };
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(10);
            writer.Write<Vector3B>(new Vector3B(x, y, z));
            writer.WriteInt(Cube);
            writer.WriteLong(0);
            writer.WriteLong(0);
            writer.WriteBool(false);
            writer.WriteFloat(0);
        }));
    }

    [Test]
    public void PlacedCube_CostsTheSalePriceInMeso_Once() {
        test.AddEvent(7001, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 1, 8000)]));
        (GameSession session, Plot plot) = StartInHome(FurnishingCurrencyType.Meso);

        Place(session, -1, -1, 1);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 200));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));

        // The next one is bought again, at the same price.
        Place(session, -2, -1, 1);
        Assert.That(plot.Cubes, Has.Count.EqualTo(2));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 400));
    }

    [Test]
    public void PlacedCube_CostsTheSalePriceInMerets_Once() {
        test.AddEvent(7001, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 3, 5000)]));
        (GameSession session, Plot plot) = StartInHome(FurnishingCurrencyType.Meret);

        Place(session, -1, -1, 1);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 500));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));
    }

    [Test]
    public void PlacedCube_TakesTheLowerEventId_OfTwoSales() {
        test.AddEvent(7002, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 1, 8000)]));
        test.AddEvent(7001, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 1, 5000)]));
        (GameSession session, Plot plot) = StartInHome(FurnishingCurrencyType.Meso);

        Place(session, -1, -1, 1);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 500));
    }

    [Test]
    public void PlacedCube_CostsTheFullPrice_WhenTheSaleIsOfAnotherMoneyOrOver() {
        // A meret row does not lower a meso price, and an ended sale lowers nothing.
        test.AddEvent(7001, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 3, 8000)]));
        test.AddEvent(7002, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 1, 8000)]),
            DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        (GameSession session, Plot plot) = StartInHome(FurnishingCurrencyType.Meso);

        Place(session, -1, -1, 1);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(plot.Cubes, Has.Count.EqualTo(1));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 1000));
    }

    [Test]
    public void PlacedCube_IsRefused_WhenTheSalePriceIsNotOwned() {
        test.AddEvent(7001, GameEventType.SaleHousing, new SaleHousing([new SaleHousingCube(Filter, 1, 8000)]));
        (GameSession session, Plot plot) = StartInHome(FurnishingCurrencyType.Meso);
        session.Currency.Meso = 199;

        Place(session, -1, -1, 1);

        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(199));
    }
    #endregion

    #region SaleUgcDesign
    private UgcHandler UgcHandler(GameSession session) => new() {
        WebStorage = new WebStorage(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, NullLogger<WebStorage>.Instance),
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        World = null!,
    };

    private GameSession StartWithDesign(MeretMarketCurrencyType currency) {
        test.SetTable(new UgcDesignTable(new Dictionary<int, UgcDesignTable.Entry> {
            [Design] = new(1, currency, 390, 0, 0),
        }));
        test.Register(TestGameSession.Metadata(Design, EquipSlot.CL));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(Town));
        session.Currency.Meso = 5000;
        session.Currency.Meret = 5000;
        test.TakeSent();
        return session;
    }

    // Ugc 1: the upload control's item uid, the upload head (kind 1, an item), then the item's part.
    private static ByteReader Upload(long cost) => TestGameSession.Packet(writer => {
        writer.WriteByte(1);
        writer.WriteLong(0);
        writer.WriteByte(1);
        writer.WriteShort(0);
        writer.WriteInt(0);
        writer.WriteLong(0);
        writer.WriteLong(0);
        writer.WriteUnicodeString("");
        writer.WriteUnicodeString("");
        writer.WriteLong(0);
        writer.WriteUnicodeString("");
        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteUnicodeString("");
        writer.WriteLong(0);
        writer.WriteInt(Design);
        writer.WriteInt(1);
        writer.WriteUnicodeString("shirt");
        writer.WriteByte(0);
        writer.WriteLong(cost);
        writer.WriteBool(false);
    });

    [Test]
    public void Upload_CostsTheSalePriceInMerets_Once() {
        test.AddEvent(7001, GameEventType.SaleUgcDesign, new SaleUgcDesign(8000));
        GameSession session = StartWithDesign(MeretMarketCurrencyType.Meret);

        UgcHandler(session).Handle(session, Upload(78));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.StagedUgcItem, Is.Not.Null);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 78));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000));

        // A second upload before the first finished gives the first fee back: one fee is held, not two.
        UgcHandler(session).Handle(session, Upload(78));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 78));
    }

    [Test]
    public void Upload_CostsTheSalePriceInMeso_OfTheHighestRunningRate() {
        test.AddEvent(7001, GameEventType.SaleUgcDesign, new SaleUgcDesign(2000));
        test.AddEvent(7002, GameEventType.SaleUgcDesign, new SaleUgcDesign(5000));
        test.AddEvent(7003, GameEventType.SaleUgcDesign, new SaleUgcDesign(9000), DateTime.Now.AddDays(-9), DateTime.Now.AddDays(-2));
        GameSession session = StartWithDesign(MeretMarketCurrencyType.Meso);

        UgcHandler(session).Handle(session, Upload(195));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(5000 - 195));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
    }

    [Test]
    public void Upload_CostsTheFullPrice_WithoutASale() {
        GameSession session = StartWithDesign(MeretMarketCurrencyType.Meret);

        UgcHandler(session).Handle(session, Upload(390));

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000 - 390));
    }
    #endregion
}
