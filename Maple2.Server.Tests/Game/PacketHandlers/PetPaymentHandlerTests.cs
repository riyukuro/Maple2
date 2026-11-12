using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// What a pet's attribute change, fusion and evolve points take, and in which order: KMS2 sends the meso update
// before the bag updates.
public class PetPaymentHandlerTests {
    private const int PetId = 60000001;
    private TestGameSession test = null!;
    private Item pet = null!;

    private static ItemMetadata Tagged(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 4, slotMax: 9999);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private static ItemMetadata PetItem() {
        ItemMetadata metadata = TestGameSession.Metadata(PetId, type: 11, slotMax: 1);
        return metadata with { Property = metadata.Property with { PetId = PetId } };
    }

    private GameSession Login(long meso, int fragments = 0, int stones = 5, int cells = 100, int pets = 1) {
        test = new TestGameSession();
        test.RegisterPet(PetId);
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { PetMaxLevel = 50 });
        test.SetTable(new PetEvolutionTable(new Dictionary<int, PetEvolutionTable.Entry> { [0] = new(0, [10, 10, 10, 10]) }));
        test.SetTable(new PetExpTable(Enumerable.Range(1, 50).ToDictionary(level => level, _ => new long[] { 1_000_000, 1_000_000, 1_000_000, 1_000_000 })));
        long[] uids = Enumerable.Range(0, pets).Select(_ => test.Store(PetItem()).Uid).ToArray();
        if (fragments > 0) {
            test.Store(Tagged(40100001, ItemTag.CrystalPiece), fragments);
        }
        if (stones > 0) {
            test.Store(Tagged(40100002, ItemTag.RedCrystal), stones);
        }
        if (cells > 0) {
            test.Store(Tagged(40100003, ItemTag.MetaCell), cells);
        }
        GameSession session = test.Start();
        session.Currency.Meso = meso;
        pet = session.Item.Inventory.Get(uids[0])!;
        Fodders = uids.Skip(1).Select(uid => session.Item.Inventory.Get(uid)!).ToArray();
        test.TakeSent();
        return session;
    }

    private Item[] Fodders = [];

    private static long Owned(GameSession session, ItemTag tag) => session.Item.Inventory.Find(tag).Sum(item => (long) item.Amount);

    private bool Pay(GameSession session) => ChangeAttributesHandler.PayChange(session, pet, ChangeAttributesHandler.GetIngredients(pet), null);

    // A rarity 1 pet's first change: 5 stones, 100 fragments, 100 cells.
    [Test]
    public void PetChange_WithAllFragmentsTakesNoMeso() {
        GameSession session = Login(meso: 50000, fragments: 150);

        Assert.That(Pay(session), Is.True);

        Assert.That(test.TakeSentOps(), Has.None.EqualTo(SendOp.Meso));
        Assert.That(session.Currency.Meso, Is.EqualTo(50000));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(50));
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(0));
        Assert.That(Owned(session, ItemTag.MetaCell), Is.EqualTo(0));
    }

    [TestCase(30, 70000)]
    [TestCase(0, 100000)]
    public void PetChange_PaysLackingFragmentsInMesoFirst(int fragments, long fee) {
        GameSession session = Login(meso: 100000, fragments: fragments);

        Assert.That(Pay(session), Is.True);

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(ops[0], Is.EqualTo(SendOp.Meso));
        Assert.That(ops.Skip(1), Is.All.EqualTo(SendOp.ItemInventory));
        Assert.That(session.Currency.Meso, Is.EqualTo(100000 - fee));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(0));
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(0));
    }

    [TestCase(69999, 5, 100)] // the meso lacks
    [TestCase(100000, 4, 100)] // a stone lacks: only fragments have a meso price
    [TestCase(100000, 5, 99)]
    public void PetChange_RefusedTakesNothing(long meso, int stones, int cells) {
        GameSession session = Login(meso: meso, fragments: 30, stones: stones, cells: cells);

        Assert.That(Pay(session), Is.False);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ChangeAttributes }));
        Assert.That(session.Currency.Meso, Is.EqualTo(meso));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(30));
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(stones));
        Assert.That(Owned(session, ItemTag.MetaCell), Is.EqualTo(cells));
    }

    // A gear change has no meso price for fragments.
    [Test]
    public void GearChange_WithoutFragmentsIsRefused() {
        GameSession session = Login(meso: 10_000_000, fragments: 1, stones: 999, cells: 999);
        Item gear = new(TestGameSession.Metadata(11300001, EquipSlot.CL), rarity: 4);
        List<IngredientInfo> price = ChangeAttributesHandler.GetIngredients(gear);
        Assert.That(price.Any(info => info.Tag == ItemTag.CrystalPiece && info.Amount > 1), Is.True);

        Assert.That(ChangeAttributesHandler.PayChange(session, gear, price, null), Is.False);

        Assert.That(session.Currency.Meso, Is.EqualTo(10_000_000));
    }

    // Evolve points: 2 fodders of rarity 1 cost 20,000 each.
    [Test]
    public void EvolvePoints_TakesTheMesoThenTheFodders() {
        GameSession session = Login(meso: 50000, pets: 3);
        pet.Pet!.Level = 50;

        new PetHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(18);
            writer.WriteLong(pet.Uid);
            writer.WriteShort(2);
            writer.WriteLong(Fodders[0].Uid);
            writer.WriteLong(Fodders[1].Uid);
        }));

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Meso, SendOp.ItemInventory, SendOp.ItemInventory, SendOp.ResponsePet }));
        Assert.That(session.Currency.Meso, Is.EqualTo(10000));
        Assert.That(pet.Pet.EvolvePoints, Is.EqualTo(2));
        Assert.That(session.Item.Inventory.Get(Fodders[0].Uid), Is.Null);
    }

    [Test]
    public void Fusion_TakesTheMesoThenTheFodder() {
        GameSession session = Login(meso: 10_000_000, pets: 2);

        new PetHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(12);
            writer.WriteLong(pet.Uid);
            writer.WriteShort(1);
            writer.WriteLong(Fodders[0].Uid);
            writer.WriteInt(1);
        }));

        List<SendOp> ops = test.TakeSentOps();
        Assert.That(string.Join(",", ops), Does.StartWith("Meso,ItemInventory"));
        Assert.That(ops, Does.Contain(SendOp.ResponsePet));
        Assert.That(session.Currency.Meso, Is.LessThan(10_000_000));
        Assert.That(session.Item.Inventory.Get(Fodders[0].Uid), Is.Null);
    }
}
