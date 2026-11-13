using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using LuaPort = Maple2.Server.Game.LuaFunctions.Lua;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// MaidCraftItem 7 (CManufactureManager::SendRequest7): a maid craft received at once, ready or not, for the merets of
// the client's Lua calcManufactureCompleteByMerat of the seconds left. The answer is MaidCraftItem 7 with the receive
// result; the merets are taken only once the craft is cleared and the whole product is in the bag. The ids are made up.
[NonParallelizable]
public class MaidCraftMeretReceiveTests {
    private const int RecipeId = 4101;
    private const int Product = 20000101;
    private const int Filler = 20000102;
    private const int Meso = 90000001;
    private const int MaidCube = 50400001;
    private long MaidUid;
    private Plot plot = null!;

    private TestGameSession test = null!;
    private GameSession session => test.Session;

    private void Login(bool fullBag = false, int slotMax = 100, int product = Product) {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        test = new TestGameSession(options);
        test.Register(TestGameSession.Metadata(Product, slotMax: slotMax));
        test.Register(TestGameSession.Metadata(MaidCube));
        test.Register(TestGameSession.Metadata(Meso));
        if (fullBag) {
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabMiscCount = [1, 0] });
            test.Store(TestGameSession.Metadata(Filler));
        }
        test.Start();
        plot = test.EnterHome();
        test.SetTable(new MaidTable(new Dictionary<int, MaidTable.Entry>(), new Dictionary<int, int>(), new Dictionary<int, MaidTable.Recipe> {
            [RecipeId] = new MaidTable.Recipe(RecipeId, [], 0, [300, 200, 100], 50, 5, product, 2, 1),
        }));
        test.TakeSent();
    }

    // A maid of the session's account placed in its home, its craft (recipe 0: none) stored with it.
    private Maid Place(int recipeId, long start, int duration) {
        Maid maid;
        using (GameStorage.Request db = test.Storage.Context()) {
            session.ItemMetadata.TryGet(MaidCube, out ItemMetadata? cubeMetadata);
            PlotCube? cube = db.CreateCube(plot, new PlotCube(cubeMetadata!) { Type = PlotCube.CubeType.Construction });
            Assert.That(cube, Is.Not.Null);
            MaidUid = cube!.Id;
            maid = db.CreateMaid(new Maid {
                Uid = MaidUid, OwnerId = session.AccountId, ItemId = MaidCube, MaidId = 3, CreationTime = 1700000000,
                ExpiryTime = DateTime.UtcNow.ToEpochSeconds() + 86400, Profile = new MaidProfile { Name = "n" },
            })!;
        }
        Assert.That(maid, Is.Not.Null);
        FieldManager field = session.Field!;
        if (recipeId != 0) {
            Assert.That(field.SetMaidCraft(maid, recipeId, start, duration), Is.True);
        }
        Type slotType = typeof(FieldManager).GetNestedType("MaidSlot", BindingFlags.NonPublic)!;
        object slot = Activator.CreateInstance(slotType, maid)!;
        object maids = typeof(FieldManager).GetField("maids", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(field)!;
        maids.GetType().GetMethod("TryAdd")!.Invoke(maids, [maid.Uid, slot]);
        return maid;
    }

    private List<byte[]> Send() {
        new MaidCraftItemHandler { TableMetadata = test.TableMetadata, ServerTableMetadata = test.ServerTableMetadata }
            .Handle(session, TestGameSession.Packet(writer => {
                writer.WriteByte(7);
                writer.WriteLong(MaidUid);
            }));
        test.RunFieldQueue();
        return test.TakeSent();
    }

    private static List<string> Crafts(List<byte[]> sent) {
        return sent.Where(p => TestGameSession.Op(p) == SendOp.MaidCraftItem).Select(Convert.ToHexString).ToList();
    }

    private (int RecipeId, long StartTime, int Duration)? Stored() {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetMaidCraft(MaidUid);
    }

    private int Held(int itemId) => session.Item.Inventory.Filter(item => item.Id == itemId).Sum(item => item.Amount);

    [TestCase(-5, 0, 0)]
    [TestCase(0, 0, 0)]
    [TestCase(1, 1, 1)]
    [TestCase(119, 1, 6)]
    [TestCase(120, 2, 7)]
    [TestCase(3599, 26, 154)]
    [TestCase(3600, 27, 157)]
    [TestCase(7199, 48, 286)]
    [TestCase(7200, 49, 289)]
    [TestCase(10799, 67, 398)]
    [TestCase(10800, 68, 403)]
    [TestCase(28799, 145, 870)]
    [TestCase(28800, 146, 871)]
    [TestCase(86400, 357, 2138)]
    public void TheCostIsTheClientLuaOfTheSecondsLeft(long seconds, long cost, long costCn) {
        string locale = LuaPort.Locale;
        try {
            LuaPort.Locale = "KR";
            long other = LuaPort.CalcManufactureCompleteByMerat(seconds);
            LuaPort.Locale = "CN";
            long cn = LuaPort.CalcManufactureCompleteByMerat(seconds);
            Assert.Multiple(() => {
                Assert.That(Math.Max(0, other), Is.EqualTo(cost));
                Assert.That(Math.Max(0, cn), Is.EqualTo(costCn));
            });
        } finally {
            LuaPort.Locale = locale;
        }
    }

    [Test]
    public void ACraftNotReadyIsReceivedForItsCost() {
        Login();
        Place(RecipeId, DateTime.UtcNow.ToEpochSeconds(), 3700);
        session.Currency.Meret = 100;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            // 3700 or 3699 seconds left: 26 + 101/120 * 0.72 rounded up.
            Assert.That(session.Currency.Meret, Is.EqualTo(100 - 27));
            Assert.That(Held(Product), Is.EqualTo(2));
            Assert.That(Stored(), Is.EqualTo((0, 0L, 0)));
            Assert.That(Crafts(sent).Take(2), Is.EqualTo(new[] {
                Convert.ToHexString(MaidCraftItemPacket.Remove(MaidUid).ToArray()),
                Convert.ToHexString(MaidCraftItemPacket.MeretReceiveResult(2).ToArray()),
            }));
            Assert.That(Crafts(sent), Has.Count.EqualTo(3));
            Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.Meret));
        });
    }

    [Test]
    public void AReadyCraftCostsNothing() {
        Login();
        Place(RecipeId, DateTime.UtcNow.ToEpochSeconds() - 5000, 300);
        session.Currency.Meret = 10;

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(10));
            Assert.That(Held(Product), Is.EqualTo(2));
            Assert.That(Crafts(sent), Does.Contain(Convert.ToHexString(MaidCraftItemPacket.MeretReceiveResult(2).ToArray())));
        });
    }

    [Test]
    public void TooFewMeretsTakeNothingAndTheCraftStays() {
        Login();
        long start = DateTime.UtcNow.ToEpochSeconds();
        Place(RecipeId, start, 3700);
        session.Currency.Meret = 26;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(26));
            Assert.That(Held(Product), Is.EqualTo(0));
            Assert.That(Stored(), Is.EqualTo((RecipeId, start, 3700)));
            Assert.That(Crafts(sent), Is.Empty);
            Assert.That(sent.Select(TestGameSession.Op), Does.Contain(SendOp.Notice));
        });
    }

    [Test]
    public void NoCraftAnswersNoRecord() {
        Login();
        Place(0, 0, 0);
        session.Currency.Meret = 100;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(100));
            Assert.That(Crafts(sent), Is.EqualTo(new[] { Convert.ToHexString(MaidCraftItemPacket.MeretReceiveResult(7).ToArray()) }));
        });
    }

    [Test]
    public void AFullBagTakesNothingAndTheCraftStays() {
        Login(fullBag: true);
        long start = DateTime.UtcNow.ToEpochSeconds();
        Place(RecipeId, start, 3700);
        session.Currency.Meret = 100;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(100));
            Assert.That(Held(Product), Is.EqualTo(0));
            Assert.That(Stored(), Is.EqualTo((RecipeId, start, 3700)));
            Assert.That(Crafts(sent), Is.Empty);
        });
    }

    // A currency product is added without a bag row, and follows the same order: the merets are taken, the mesos given.
    [Test]
    public void ACurrencyProductIsChargedAndGiven() {
        Login(product: Meso);
        Place(RecipeId, DateTime.UtcNow.ToEpochSeconds(), 3700);
        session.Currency.Meret = 100;
        long mesos = session.Currency.Meso;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(100 - 27));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos + 2));
            Assert.That(Stored(), Is.EqualTo((0, 0L, 0)));
            Assert.That(Crafts(sent), Does.Contain(Convert.ToHexString(MaidCraftItemPacket.MeretReceiveResult(2).ToArray())));
        });
    }

    [Test]
    public void ACurrencyProductIsNotGivenWithTooFewMerets() {
        Login(product: Meso);
        long start = DateTime.UtcNow.ToEpochSeconds();
        Place(RecipeId, start, 3700);
        session.Currency.Meret = 26;
        long mesos = session.Currency.Meso;
        test.TakeSent();

        List<byte[]> sent = Send();

        Assert.Multiple(() => {
            Assert.That(session.Currency.Meret, Is.EqualTo(26));
            Assert.That(session.Currency.Meso, Is.EqualTo(mesos));
            Assert.That(Stored(), Is.EqualTo((RecipeId, start, 3700)));
            Assert.That(Crafts(sent), Is.Empty);
        });
    }

    // What an add put in comes out again: the stack it filled goes back to its amount, the new row goes.
    [Test]
    public void TakeBackUndoesAnAdd() {
        Login(slotMax: 4);
        Item? stack = session.Field!.ItemDrop.CreateItem(Product, 1, 3);
        Assert.That(session.Item.Inventory.Add(stack!), Is.True);
        Dictionary<long, int> held = session.Item.Inventory.Filter(item => item.Id == Product).ToDictionary(item => item.Uid, item => item.Amount);
        var added = new List<Item>();

        Assert.That(session.Item.Inventory.Add(session.Field.ItemDrop.CreateItem(Product, 1, 3)!, onAdded: item => added.Add(item)), Is.True);
        Assert.That(Held(Product), Is.EqualTo(6));

        Assert.That(MaidCraftItemHandler.TakeBack(session, added, held), Is.True);
        Assert.Multiple(() => {
            List<Item> left = session.Item.Inventory.Filter(item => item.Id == Product).ToList();
            Assert.That(left, Has.Count.EqualTo(1));
            Assert.That(left[0].Amount, Is.EqualTo(3));
        });
    }

    [Test]
    public void TheMode7AnswerIsTheReceiveResultUnderMode7() {
        Assert.Multiple(() => {
            Assert.That(MaidCraftItemPacket.MeretReceiveResult(2).ToArray().Skip(2), Is.EqualTo(new byte[] { 7, 2, 0, 0, 0 }));
            Assert.That(MaidCraftItemPacket.ReceiveResult(2).ToArray().Skip(2), Is.EqualTo(new byte[] { 6, 2, 0, 0, 0 }));
        });
    }
}
