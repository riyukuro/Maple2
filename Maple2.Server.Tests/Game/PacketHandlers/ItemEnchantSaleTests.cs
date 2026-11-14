using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A running SaleEnchant event and the character's ReduceEnchantMaterialFee (65) lower an Ophelia or Peachy try's
// materials. The enchant window only shows the sum as a sale text and tests the bag against the counts of the
// staged item's reply, so the reply carries the lowered counts and the try charges the same.
public class ItemEnchantSaleTests {
    private const int OnyxId = 40100023, ChaosOnyxId = 40100024, FragmentId = 40100001;
    private TestGameSession test = null!;
    private Item gear = null!;

    private static ItemMetadata Material(int id, ItemTag tag) {
        ItemMetadata metadata = TestGameSession.Metadata(id, type: 2, slotMax: 30000);
        return metadata with { Property = metadata.Property with { Tag = tag } };
    }

    private GameSession Login(int saleRate = 0, int fee = 0, float reduceLimit = 0.5f, bool ended = false, int enchants = 0) {
        test = new TestGameSession();
        gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.Store(Material(OnyxId, ItemTag.Onix), amount: 30000);
        test.Store(Material(ChaosOnyxId, ItemTag.ChaosOnix), amount: 30000);
        test.Store(Material(FragmentId, ItemTag.CrystalPiece), amount: 30000);
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { IngredientFeeReduceLimit = reduceLimit });
        if (saleRate > 0) {
            test.AddEvent(7300, GameEventType.SaleEnchant, new EventRate(saleRate), end: ended ? System.DateTime.Now.AddDays(-1) : null);
            test.AddEvent(7301, GameEventType.SaleEnchant, new EventRate(saleRate / 2)); // the highest rate counts
        }
        GameSession session = test.Start();
        Item item = session.Item.Inventory.Get(gear.Uid)!;
        item.Rarity = 4;
        item.Enchant = new ItemEnchant(enchants: enchants);
        if (fee > 0) {
            session.Player.Stats.Values[SpecialAttribute.ReduceEnchantMaterialFee].AddTotal(fee);
        }
        test.TakeSent();
        return session;
    }

    private static int Held(GameSession session, ItemTag tag) => session.Item.Inventory.Find(tag).Sum(item => item.Amount);

    // The materials of the staged item's reply: mode, type, uid, then a count byte and per entry 0, tag, amount.
    private static Dictionary<int, int> StagedAmounts(IEnumerable<byte[]> sent) {
        byte[] stage = sent.Last(p => (SendOp) (p[0] | p[1] << 8) == SendOp.ItemEnchant);
        var reader = new ByteReader(stage, 2);
        reader.ReadByte();
        reader.ReadShort();
        reader.ReadLong();
        int count = reader.ReadByte();
        var amounts = new Dictionary<int, int>();
        for (int i = 0; i < count; i++) {
            reader.ReadInt();
            int tag = reader.ReadInt();
            amounts[tag] = reader.ReadInt();
        }
        return amounts;
    }

    private Dictionary<int, int> FullAmounts(EnchantType type, int enchants = 0) {
        GameSession plain = Login(enchants: enchants);
        Assert.That(plain.ItemEnchant.StageItem(type, gear.Uid), Is.True);
        return StagedAmounts(test.TakeSent());
    }

    // KR rates are 1000, 3000 and 5000; the fee attribute is out of 1000 and adds to the rate (100 = 10%); the sum is
    // held to IngredientFeeReduceLimit (KR 0.5).
    [TestCase(EnchantType.Ophelia, 3000, 0, 3000)]
    [TestCase(EnchantType.Ophelia, 0, 100, 1000)]
    [TestCase(EnchantType.Ophelia, 3000, 100, 4000)]
    [TestCase(EnchantType.Ophelia, 5000, 100, 5000)] // held to the limit
    [TestCase(EnchantType.Peachy, 1000, 0, 1000)]
    [TestCase(EnchantType.Peachy, 5000, 300, 5000)]
    public void Stage_SendsTheLoweredCounts(EnchantType type, int saleRate, int fee, int discount) {
        Dictionary<int, int> full = FullAmounts(type);
        GameSession session = Login(saleRate, fee);

        Assert.That(session.ItemEnchant.StageItem(type, gear.Uid), Is.True);

        Dictionary<int, int> staged = StagedAmounts(test.TakeSent());
        Assert.That(staged.Keys, Is.EquivalentTo(full.Keys));
        foreach ((int tag, int amount) in full) {
            Assert.That(staged[tag], Is.EqualTo(EventSaleRules.RemakeIngredientCount(amount, discount, 0.5f)), $"tag {tag}");
            Assert.That(staged[tag], Is.LessThan(amount).Or.EqualTo(1), $"tag {tag}");
        }
    }

    // No event, an ended event or no attribute: the full counts.
    [TestCase(0, false)]
    [TestCase(5000, true)]
    public void Stage_NoRunningSaleSendsTheFullCounts(int saleRate, bool ended) {
        Dictionary<int, int> full = FullAmounts(EnchantType.Ophelia);
        GameSession session = Login(saleRate, ended: ended);
        // The second (half rate) event still runs when the first has ended.
        int discount = saleRate > 0 && ended ? saleRate / 2 : 0;

        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);

        Dictionary<int, int> staged = StagedAmounts(test.TakeSent());
        foreach ((int tag, int amount) in full) {
            Assert.That(staged[tag], Is.EqualTo(EventSaleRules.RemakeIngredientCount(amount, discount, 0.5f)), $"tag {tag}");
        }
    }

    // The try charges the counts it staged.
    [TestCase(EnchantType.Ophelia)]
    [TestCase(EnchantType.Peachy)]
    public void Enchant_ChargesTheLoweredCounts(EnchantType type) {
        GameSession session = Login(saleRate: 3000, fee: 100);
        Assert.That(session.ItemEnchant.StageItem(type, gear.Uid), Is.True);
        Dictionary<int, int> staged = StagedAmounts(test.TakeSent());

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.True);

        Assert.Multiple(() => {
            Assert.That(Held(session, ItemTag.Onix), Is.EqualTo(30000 - staged.GetValueOrDefault(ItemTagMap.ToClient(ItemTag.Onix))));
            Assert.That(Held(session, ItemTag.ChaosOnix), Is.EqualTo(30000 - staged.GetValueOrDefault(ItemTagMap.ToClient(ItemTag.ChaosOnix))));
            Assert.That(Held(session, ItemTag.CrystalPiece), Is.EqualTo(30000 - staged.GetValueOrDefault(ItemTagMap.ToClient(ItemTag.CrystalPiece))));
            Assert.That(Held(session, ItemTag.Onix), Is.GreaterThan(30000 - FullAmounts(type).GetValueOrDefault(ItemTagMap.ToClient(ItemTag.Onix))));
        });
    }

    // A SaleEnchant row (KR value1 1000, 3000 or 5000) is read as its rate; every KR row has ended, so the live table
    // has none and an operator's own row is what this reads.
    [TestCase("3000", 3000)]
    [TestCase("0", null)]
    public void Ingest_ReadsTheRowAsARate(string value1, int? rate) {
        var mapper = (Maple2.File.Ingest.Mapper.ServerTableMapper) System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Maple2.File.Ingest.Mapper.ServerTableMapper));
        System.Reflection.MethodInfo parse = typeof(Maple2.File.Ingest.Mapper.ServerTableMapper).GetMethod("ParseGameEventData",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        object? data = parse.Invoke(mapper, [GameEventType.SaleEnchant, value1, "", "", ""]);

        Assert.That(data, Is.EqualTo(rate == null ? null : new EventRate(rate.Value)));
    }

    [TestCase(0, 0, 0)]
    [TestCase(3000, 0, 3000)]
    [TestCase(0, 100, 1000)]
    [TestCase(3000, 100, 4000)]
    [TestCase(-5, 50, 500)]
    public void EnchantDiscount_AddsTheRateAndTheAttribute(int saleRate, long fee, int expected) {
        Assert.That(EventSaleRules.EnchantDiscount(saleRate, fee), Is.EqualTo(expected));
    }
}
