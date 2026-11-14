using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The bill for a saved layout (RequestCube 35, answered with ResponseCube 0x24) or a blueprint lists only the money
// types that cost something. The client opens its bill only for a bill with entries and applies a bill of none at
// once (uiugccubebilldialog.cpp:248, :297), so a layout whose cubes are all in storage must send no entries; it sent
// a meso and a meret entry of 0, and the client showed a bill of nothing to pay. The item ids are made up.
public class LayoutBillTests {
    private const int MesoCube = 50200921, MeretCube = 50200922;
    private TestGameSession test = null!;

    private static ItemMetadata Furnishing(int id) => TestGameSession.Metadata(id) with {
        Housing = new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false),
        Install = new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none),
    };

    private GameSession StartInHome(params int[] layoutCubes) => StartPriced(1000, layoutCubes);

    private GameSession StartPriced(int mesoPrice, params int[] layoutCubes) {
        test = new TestGameSession();
        test.SetTable(new UgcHousingPointRewardTable(new Dictionary<int, UgcHousingPointRewardTable.Entry>()));
        test.SetTable(new MasteryUgcHousingTable(new Dictionary<int, MasteryUgcHousingTable.Entry> { [1] = new(1, 0, 0) }));
        test.SetTable(new FurnishingShopTable(new Dictionary<int, FurnishingShopTable.Entry> {
            [MesoCube] = new(MesoCube, true, FurnishingCurrencyType.Meso, mesoPrice),
            [MeretCube] = new(MeretCube, true, FurnishingCurrencyType.Meret, 30),
        }));
        test.SetServerTable(new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>()));
        test.Register(Furnishing(MesoCube), Furnishing(MeretCube));
        GameSession session = test.Start();
        session.Housing.InitNewHome("home", null);
        test.EnterHome();
        List<PlotCube> cubes = layoutCubes.Select(id => {
            session.ItemMetadata.TryGet(id, out ItemMetadata? metadata);
            return new PlotCube(metadata!) { Type = PlotCube.CubeType.Default };
        }).ToList();
        session.Player.Value.Home.Layouts.Add(new HomeLayout(1, "layout", 10, 4, DateTimeOffset.UnixEpoch, cubes));
        return session;
    }

    private (int Count, int Cubes, Dictionary<byte, long> Costs) RequestBill(GameSession session) {
        test.TakeSent();
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(35);
            writer.WriteInt(1);
        }));
        byte[][] sent = test.TakeSent().ToArray();
        Assert.That(sent, Has.Length.EqualTo(1));
        var reader = new ByteReader(sent[0], 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x24));
        int count = reader.ReadByte();
        int cubes = reader.ReadInt();
        var costs = new Dictionary<byte, long>();
        for (int i = 0; i < count; i++) {
            costs[reader.ReadByte()] = reader.ReadLong();
        }
        Assert.That(reader.Available, Is.EqualTo(0));
        return (count, cubes, costs);
    }

    // The bill is a long per money type; the price times the count was int math, so two cubes of 2^30 meso wrapped
    // to a negative total and the meso entry was dropped from the bill.
    [Test]
    public void ALargeTotal_DoesNotWrap() {
        const int price = 1 << 30;
        GameSession session = StartPriced(price, MesoCube, MesoCube);

        (int count, int cubes, Dictionary<byte, long> costs) = RequestBill(session);
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(cubes, Is.EqualTo(2));
            Assert.That(costs[(byte) FurnishingCurrencyType.Meso], Is.EqualTo(2L * price));
        });
    }

    [Test]
    public void AllCubesInStorage_SendsNoEntries() {
        GameSession session = StartInHome(MesoCube, MeretCube);
        Assert.That(session.Item.Furnishing.AddCube(MesoCube), Is.Not.Zero);
        Assert.That(session.Item.Furnishing.AddCube(MeretCube), Is.Not.Zero);

        (int count, int cubes, _) = RequestBill(session);
        Assert.Multiple(() => {
            Assert.That(count, Is.Zero);
            Assert.That(cubes, Is.Zero);
        });
    }

    [Test]
    public void OnlyMesoToPay_SendsTheMesoEntryOnly() {
        GameSession session = StartInHome(MesoCube, MesoCube, MeretCube);
        Assert.That(session.Item.Furnishing.AddCube(MeretCube), Is.Not.Zero);

        (int count, int cubes, Dictionary<byte, long> costs) = RequestBill(session);
        Assert.Multiple(() => {
            Assert.That(count, Is.EqualTo(1));
            Assert.That(cubes, Is.EqualTo(2));
            Assert.That(costs, Is.EqualTo(new Dictionary<byte, long> { [(byte) FurnishingCurrencyType.Meso] = 2000 }));
        });
    }

    // A layout bill (35) or blueprint load (65) of a slot the server has no row in is answered with 0x02 s_ugcmap_db,
    // as a layout load (48) is; both got no answer before.
    [TestCase(35)]
    [TestCase(65)]
    public void MissingSlot_IsAnsweredWithTheDbError(int command) {
        GameSession session = StartInHome(MesoCube);
        test.TakeSent();
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte((byte) command);
            writer.WriteInt(7);
        }));
        byte[] error = CubePacket.Error(UgcMapError.s_ugcmap_db).ToArray();
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(error) }));
    }

    // A layout (46) or blueprint (64) save the database refuses is answered with the save's own command and the
    // error byte, 0x2e or 0x40 s_ugcmap_db, as the client reads it; it was 0x02 (the plot purchase answer).
    [TestCase(46, false)]
    [TestCase(64, true)]
    public void FailedSave_IsAnsweredWithTheSaveCommand(int command, bool blueprint) {
        GameSession session = StartInHome();
        test.TakeSent();
        test.FailSave = context => context.ChangeTracker.Entries()
            .Any(entry => entry.State == EntityState.Added && entry.Entity is Maple2.Database.Model.HomeLayout);
        new RequestCubeHandler { TableMetadata = test.TableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte((byte) command);
            writer.WriteInt(2);
            writer.WriteUnicodeString("layout");
        }));
        test.FailSave = null;
        byte[] error = CubePacket.SaveLayoutError(UgcMapError.s_ugcmap_db, blueprint).ToArray();
        Assert.Multiple(() => {
            Assert.That(error[2], Is.EqualTo(command));
            Assert.That(error[3], Is.EqualTo(13));
            Assert.That(error, Has.Length.EqualTo(4));
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Convert.ToHexString(error) }));
        });
    }

    // The blueprint bill (ResponseCube 0x43 from a blueprint item) comes from the same RequestLayout.
    [Test]
    public void BlueprintBill_OfOwnedCubes_HasNoEntries() {
        GameSession session = StartInHome(MeretCube);
        Assert.That(session.Item.Furnishing.AddCube(MeretCube), Is.Not.Zero);

        Assert.That(session.Housing.RequestLayout(session.Player.Value.Home.Layouts[0], out (Dictionary<FurnishingCurrencyType, long> cubeCosts, int cubeCount) result), Is.True);
        Assert.Multiple(() => {
            Assert.That(result.cubeCosts, Is.Empty);
            Assert.That(result.cubeCount, Is.Zero);
        });
    }
}
