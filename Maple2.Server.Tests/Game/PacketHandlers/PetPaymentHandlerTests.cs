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

    // The events the next Login gives the server: the session takes them as it starts.
    private readonly List<(int Id, SaleRemake Sale, bool Over)> sales = [];

    private GameSession Login(long meso, int fragments = 0, int stones = 5, int cells = 100, int pets = 1) {
        test = new TestGameSession();
        foreach ((int id, SaleRemake sale, bool over) in sales) {
            test.AddEvent(id, GameEventType.SaleRemake, sale, over ? DateTime.Now.AddDays(-9) : null, over ? DateTime.Now.AddDays(-2) : null);
        }
        sales.Clear();
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

    private bool PayOnSale(GameSession session) => ChangeAttributesHandler.PayChange(session, pet,
        ChangeAttributesHandler.SaleIngredients(session, pet, ChangeAttributesHandler.GetIngredients(pet)), null);

    // Under a running change of attributes sale the change takes the lowered counts; the constants name no limit,
    // so the client's 0.5 holds the rate.
    [TestCase(5000, 3, 100, 50)] // 5 stones: 2.5 cut to 2
    [TestCase(3000, 2, 80, 30)] // 3.5 cut to 3
    [TestCase(9000, 3, 100, 50)] // held to the half
    public void PetChange_UnderASale_TakesTheLoweredCounts(int rate, int stonesLeft, int fragmentsLeft, int cellsLeft) {
        sales.Add((7001, new SaleRemake(rate, 0), false));
        GameSession session = Login(meso: 50000, fragments: 150);

        Assert.That(PayOnSale(session), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.Currency.Meso, Is.EqualTo(50000));
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(stonesLeft));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(fragmentsLeft));
        Assert.That(Owned(session, ItemTag.MetaCell), Is.EqualTo(cellsLeft));
    }

    // The pet fee attribute (173) in thousandths is a rate like the sale's; the higher of the two counts, and the
    // item fee attribute (169) is not a pet's.
    [TestCase(0, 300, 0, 2, 80, 30)] // 30% by the attribute alone
    [TestCase(3000, 400, 0, 2, 90, 40)] // the attribute above the sale: 5 * 0.6 = 3
    [TestCase(5000, 300, 0, 3, 100, 50)] // the sale above the attribute
    [TestCase(0, 900, 0, 3, 100, 50)] // held to the half
    [TestCase(0, 0, 500, 0, 50, 0)] // the item attribute: nothing off a pet
    public void PetChange_TakesTheHigherOfTheSaleAndTheFeeAttribute(int rate, int petFee, int itemFee, int stonesLeft, int fragmentsLeft, int cellsLeft) {
        if (rate > 0) {
            sales.Add((7001, new SaleRemake(rate, 0), false));
        }
        GameSession session = Login(meso: 50000, fragments: 150);
        session.Stats.Values[SpecialAttribute.ReducePetRemakeOptionFee].AddTotal(petFee);
        session.Stats.Values[SpecialAttribute.ReduceRemakeOptionFee].AddTotal(itemFee);

        Assert.That(PayOnSale(session), Is.True);

        Assert.That(test.Errors, Is.Empty);
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(stonesLeft));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(fragmentsLeft));
        Assert.That(Owned(session, ItemTag.MetaCell), Is.EqualTo(cellsLeft));
    }

    [Test]
    public void PetChange_TakesTheFullCounts_WhenTheSaleIsOverOrOfAnotherClass() {
        sales.Add((7001, new SaleRemake(5000, 0), true));
        sales.Add((7002, new SaleRemake(5000, 1), false));
        GameSession session = Login(meso: 50000, fragments: 150);

        Assert.That(PayOnSale(session), Is.True);

        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(0));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(50));
        Assert.That(Owned(session, ItemTag.MetaCell), Is.EqualTo(0));
    }

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

    // A gear change pays lacking fragments in meso as a pet change does
    // (UIItemRemakeOptionDialog::CanPayRemakePrice, itemremakeoptiondialog.cpp:797-800).
    [Test]
    public void GearChange_LackingFragmentsArePaidInMeso() {
        GameSession session = Login(meso: 10_000_000, fragments: 1, stones: 999, cells: 999);
        Item gear = new(TestGameSession.Metadata(11300001, EquipSlot.CL), rarity: 4);
        List<IngredientInfo> price = ChangeAttributesHandler.GetIngredients(gear);
        int need = price.Single(info => info.Tag == ItemTag.CrystalPiece).Amount;
        Assert.That(need, Is.GreaterThan(1));

        Assert.That(ChangeAttributesHandler.PayChange(session, gear, price, null), Is.True);

        Assert.That(test.TakeSentOps().First(), Is.EqualTo(SendOp.Meso));
        Assert.That(session.Currency.Meso, Is.EqualTo(10_000_000 - (need - 1) * 1000L));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(0));
    }

    // No fragment at all: the whole count is paid in meso and no refusal is sent.
    [Test]
    public void GearChange_WithoutAnyFragmentIsPaidInMeso() {
        GameSession session = Login(meso: 10_000_000, fragments: 0, stones: 999, cells: 999);
        Item gear = new(TestGameSession.Metadata(11300001, EquipSlot.CL), rarity: 4);
        List<IngredientInfo> price = ChangeAttributesHandler.GetIngredients(gear);
        int need = price.Single(info => info.Tag == ItemTag.CrystalPiece).Amount;

        Assert.That(ChangeAttributesHandler.PayChange(session, gear, price, null), Is.True);

        List<SendOp> sent = test.TakeSentOps();
        Assert.That(sent.First(), Is.EqualTo(SendOp.Meso));
        Assert.That(sent, Does.Not.Contain(SendOp.ChangeAttributes));
        Assert.That(session.Currency.Meso, Is.EqualTo(10_000_000 - need * 1000L));
        Assert.That(Owned(session, ItemTag.CrystalPiece), Is.EqualTo(0));
    }

    [Test]
    public void GearChange_RefusedWhenTheMesoLacks() {
        GameSession session = Login(meso: 999, fragments: 0, stones: 999, cells: 999);
        Item gear = new(TestGameSession.Metadata(11300001, EquipSlot.CL), rarity: 4);

        Assert.That(ChangeAttributesHandler.PayChange(session, gear, ChangeAttributesHandler.GetIngredients(gear), null), Is.False);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ChangeAttributes }));
        Assert.That(session.Currency.Meso, Is.EqualTo(999));
        Assert.That(Owned(session, ItemTag.RedCrystal), Is.EqualTo(999));
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

    // The points gained count for pet_evolution_point_by_rank of the pet's rarity (23100387 "with a normal pet":
    // code 1); a trophy of another rarity counts nothing.
    [Test]
    public void EvolvePoints_CountsThePointsGainedByThePetsRarity() {
        GameSession session = Login(meso: 50000, pets: 3);
        pet.Pet!.Level = 50;
        pet.Pet.EvolvePoints = 3;
        test.AddAchievement(
            TestGameSession.Trophy(23100387, ConditionType.pet_evolution_point_by_rank, [1, 10], codes: [pet.Rarity]),
            TestGameSession.Trophy(23100388, ConditionType.pet_evolution_point_by_rank, [1, 10], codes: [pet.Rarity + 1]));

        new PetHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(18);
            writer.WriteLong(pet.Uid);
            writer.WriteShort(2);
            writer.WriteLong(Fodders[0].Uid);
            writer.WriteLong(Fodders[1].Uid);
        }));

        Assert.That(pet.Pet.EvolvePoints, Is.EqualTo(5));
        Assert.That(test.TrophyCounter(23100387), Is.EqualTo(2));
        Assert.That(test.TrophyCounter(23100388), Is.Null);
    }

    // An evolution counts for pet_evolution_by_rank of the rarity reached (23100383 "Evolve normal pets to rare": code 2).
    [Test]
    public void Evolve_CountsTheRarityReached() {
        GameSession session = Login(meso: 0);
        pet.Pet!.Level = 50;
        pet.Pet.EvolvePoints = 10;
        int rarity = pet.Rarity;
        test.AddAchievement(
            TestGameSession.Trophy(23100383, ConditionType.pet_evolution_by_rank, [1, 3], codes: [rarity + 1]),
            TestGameSession.Trophy(23100384, ConditionType.pet_evolution_by_rank, [1, 3], codes: [rarity + 2]));

        new PetHandler { BanWordStorage = null! }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(17);
            writer.WriteLong(pet.Uid);
        }));

        Assert.That(pet.Rarity, Is.EqualTo(rarity + 1));
        Assert.That(test.TrophyCounter(23100383), Is.EqualTo(1));
        Assert.That(test.TrophyCounter(23100384), Is.Null);
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
