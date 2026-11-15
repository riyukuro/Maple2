using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Core.Constants;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The fee paths of a new beauty item (Beauty 3) and of a random hair pick (Beauty 7) that BeautyFeeStoreTests does not
// cover: a voucher paid fee, the random hair's store, a fee given back when the stored item cannot go on, and the
// worn face given up with a full outfit bag.
public class BeautyFeePathTests {
    private const int ShopId = 501;
    private const int Face = 10300901;
    private const int NewFace = 10300902;
    private const int Voucher = 20300001;
    private const int VoucherCouponId = 20300002;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item worn = null!;
    private Item voucher = null!;

    private static readonly EquipColor Red = new(new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), new Color(0, 0, 255, 255), 1, 3);

    // A face shop (style fee 1000 meso) whose voucher is a beauty_face item, with the face row NewFace; the
    // character wears Face and holds 3 vouchers. fullOutfitBag: one outfit bag slot, taken.
    private void Start(ItemMetadata? newFace = null, bool fullOutfitBag = false) {
        test = new TestGameSession();
        worn = test.Store(TestGameSession.Metadata(Face, EquipSlot.FA), 1, ItemGroup.Outfit, EquipSlot.FA);
        test.Register(newFace ?? TestGameSession.Metadata(NewFace, EquipSlot.FA));
        ItemMetadata coupon = TestGameSession.Metadata(Voucher);
        voucher = test.Store(coupon with { Property = coupon.Property with { Tag = ItemTag.beauty_face } }, 3);
        if (fullOutfitBag) {
            ItemMetadata outfit = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
            test.Store(outfit with { Property = outfit.Property with { IsSkin = true } });
            test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabSkinCount = [1, 0] });
        }
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        var row = new BeautyShopItem(NewFace, style, 1, 0, 0, 0, ShopItemLabel.None);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(ShopId, BeautyShopCategory.Face, 0, style, color, false, false, 0,
            VoucherCouponId, ItemTag.beauty_face, [row], []), [row]) {
            Type = BeautyShopType.Default,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private BeautyHandler Handler() => new() {
        ItemMetadata = session.ItemMetadata,
        TableMetadata = test.TableMetadata,
        NpcMetadata = null!,
        ServerTableMetadata = test.ServerTableMetadata,
    };

    // Beauty 3: the shop byte, the voucher flag, the row's item id, the shop answer's long, then the face's appearance.
    private void Create(bool useVoucher, bool failSave = false) {
        if (failSave) {
            test.FailSave = _ => true;
        }
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(3);
            writer.WriteByte(0);
            writer.WriteBool(useVoucher);
            writer.WriteInt(NewFace);
            writer.WriteLong(0);
            writer.WriteClass<ItemAppearance>(new ItemAppearance(Red));
        }));
        test.FailSave = null;
    }

    private int StoredAmount(long uid) {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking().Single(item => item.Id == uid).Amount;
    }

    private (long OwnerId, int Count) StoredFace(int itemId) {
        using var context = new Ms2Context(test.Options);
        List<Maple2.Database.Model.Item> rows = context.Item.AsNoTracking().Where(item => item.ItemId == itemId).ToList();
        return (rows.FirstOrDefault()?.OwnerId ?? -1, rows.Count);
    }

    private long StoredMeso() {
        using var context = new Ms2Context(test.Options);
        return context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId).Currency.Meso;
    }

    // A voucher pays one beauty_face item instead of the style fee: the stack is written in the face's save, taken
    // from the bag once kept, and the client gets the voucher answer (Beauty Voucher: the shop's coupon id, 1).
    [Test]
    public void AVoucherFeeIsStoredWithTheFace() {
        Start();

        Create(useVoucher: true);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Id, Is.EqualTo(NewFace));
            Assert.That(session.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(2));
            Assert.That(StoredAmount(voucher.Uid), Is.EqualTo(2));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Does.Contain(System.Convert.ToHexString(BeautyPacket.Voucher(VoucherCouponId, 1).ToArray())));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A refused save takes no voucher and keeps the worn face.
    [Test]
    public void ARefusedSaveTakesNoVoucher() {
        Start();

        Create(useVoucher: true, failSave: true);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Uid, Is.EqualTo(worn.Uid));
            Assert.That(session.Item.Inventory.Get(voucher.Uid)?.Amount, Is.EqualTo(3));
            Assert.That(StoredAmount(voucher.Uid), Is.EqualTo(3));
            Assert.That(StoredFace(NewFace).Count, Is.EqualTo(0));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Has.None.EqualTo(SendOp.Beauty));
        });
    }

    // A face that cannot go on once stored (here its slots name a one-handed slot as well, which the equip refuses;
    // any refusal after the store takes this path): its row leaves the character and its fee comes back.
    [Test]
    public void AStoredFaceThatCannotGoOnGivesTheFeeBack() {
        Start(TestGameSession.Metadata(NewFace, EquipSlot.FA) with { SlotNames = [EquipSlot.FA, EquipSlot.OH] });

        Create(useVoucher: false);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Uid, Is.EqualTo(worn.Uid));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
            Assert.That(StoredMeso(), Is.EqualTo(5000L));
            Assert.That(StoredFace(NewFace), Is.EqualTo((0L, 1)));
        });
    }

    // The same with a voucher: the voucher comes back as a new stack.
    [Test]
    public void AStoredFaceThatCannotGoOnGivesTheVoucherBack() {
        Start(TestGameSession.Metadata(NewFace, EquipSlot.FA) with { SlotNames = [EquipSlot.FA, EquipSlot.OH] });

        Create(useVoucher: true);

        int held = session.Item.Inventory.Find(Voucher).Sum(item => item.Amount);
        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Uid, Is.EqualTo(worn.Uid));
            Assert.That(held, Is.EqualTo(3));
            Assert.That(StoredFace(NewFace), Is.EqualTo((0L, 1)));
        });
    }

    // The worn face is given up, not put in the bag: a full outfit bag does not refuse the change, and the old face
    // is neither worn nor held.
    [Test]
    public void AFullOutfitBagDiscardsTheWornFace() {
        Start(fullOutfitBag: true);
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Outfit), Is.EqualTo(0));

        Create(useVoucher: false);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.FA)?.Id, Is.EqualTo(NewFace));
            Assert.That(session.Item.Inventory.Get(worn.Uid), Is.Null);
            Assert.That(session.Item.Equips.Get(worn.Uid), Is.Null);
            Assert.That(session.Currency.Meso, Is.EqualTo(4000L));
        });
        using (GameStorage.Request db = session.GameStorage.Context()) {
            session.Item.Save(db);
        }
        Assert.That(StoredFace(Face).OwnerId, Is.EqualTo(0L));
    }

    // Hair, ears, a face and face decoration come off with a full bag (given up); another outfit slot stays on.
    [TestCase(EquipSlot.HR, 10200001, true)]
    [TestCase(EquipSlot.FA, 10300001, true)]
    [TestCase(EquipSlot.FD, 10400001, true)]
    [TestCase(EquipSlot.CP, 11300001, false)]
    public void AFullOutfitBagGivesUpOnlyTheBeautySlots(EquipSlot slot, int itemId, bool comesOff) {
        test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(itemId, slot), 1, ItemGroup.Outfit, slot);
        ItemMetadata outfit = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
        test.Store(outfit with { Property = outfit.Property with { IsSkin = true } });
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { bagSlotTabSkinCount = [1, 0] });
        test.Start();
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Outfit), Is.EqualTo(0));

        bool off = session.Item.Equips.Unequip(item.Uid);

        Assert.Multiple(() => {
            Assert.That(off, Is.EqualTo(comesOff));
            Assert.That(session.Item.Equips.Get(item.Uid) == null, Is.EqualTo(comesOff));
            Assert.That(session.Item.Inventory.Get(item.Uid), Is.Null);
        });
    }

    // Beauty 7 in a random hair shop (style fee 300 meso): the new hair and its fee are stored in one save, the hair
    // worn before is kept for the answer (Beauty RandomHair: old id, new id).
    private void StartRandom() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(10200001, EquipSlot.HR) with { DefaultHairs = [new DefaultHairMetadata(MinScale: 0.5f, MaxScale: 1f)] });
        worn = test.Store(TestGameSession.Metadata(10200002, EquipSlot.HR), 1, ItemGroup.Outfit, EquipSlot.HR);
        test.SetTable(new ColorPaletteTable(new Dictionary<int, IReadOnlyDictionary<int, ColorPaletteTable.Entry>> {
            [Constant.HairPaletteId] = new Dictionary<int, ColorPaletteTable.Entry> {
                [0] = new(default, default, default, 0, 0),
            },
        }));
        test.SetTable(new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>()));
        test.SetTable(new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>()));
        test.SetTable(new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.SetTable(new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>()));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        session.Beauty = new BeautyManager(session);
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 300, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 0, string.Empty, 0);
        var row = new BeautyShopItem(10200001, style, 1, 0, 0, 0, ShopItemLabel.None);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(504, BeautyShopCategory.Hair, 0, style, color, false, false, 0, 0,
            ItemTag.None, [row], []), [row]) {
            Type = BeautyShopType.Random,
        };
        session.Currency.Meso = 5000;
        test.TakeSent();
    }

    private void Pick(bool failSave = false) {
        if (failSave) {
            test.FailSave = _ => true;
        }
        Handler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteInt(504);
            writer.WriteBool(false);
            writer.WriteLong(0);
        }));
        test.FailSave = null;
    }

    [Test]
    public void ARandomHairIsStoredWithItsFee() {
        StartRandom();

        Pick();

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Id, Is.EqualTo(10200001));
            Assert.That(session.Currency.Meso, Is.EqualTo(4700L));
            Assert.That(StoredMeso(), Is.EqualTo(4700L));
            Assert.That(StoredFace(10200001), Is.EqualTo((test.CharacterId, 1)));
            Assert.That(test.TakeSent().Select(System.Convert.ToHexString),
                Does.Contain(System.Convert.ToHexString(BeautyPacket.RandomHair(10200002, 10200001).ToArray())));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A refused save keeps the fee and the worn hair, sends no pick and leaves nothing to answer.
    [Test]
    public void ARefusedRandomHairSaveKeepsTheFeeAndTheWornHair() {
        StartRandom();

        Pick(failSave: true);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(worn.Uid));
            Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
            Assert.That(StoredFace(10200001).Count, Is.EqualTo(0));
            Assert.That(test.TakeSent().Select(TestGameSession.Op), Has.None.EqualTo(SendOp.Beauty));
        });
    }
}
