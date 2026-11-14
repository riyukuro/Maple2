using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The saved style list of a style save npc (Beauty 16 save, 17 save with an extension, 18 delete, 19 extend, 21 apply;
// uibeautyshopstyledialog.cpp:444-1742). Each case is the KR use: save the worn hair, buy slots with merets when the
// list is full, delete a saved style, put a saved style back on for the shop's style fee.
public class BeautyStyleSaveTests {
    private const int HairId = 10200002;
    private const int OtherHairId = 10200003;
    private const long Price = 980;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private Item hair = null!;

    // baseSlots saved style slots, extensions of one slot each up to maxExtra slots, for Price merets.
    // hairGender: the worn hair's gender (the character is male).
    private void Start(BeautyShopType type, int baseSlots = 30, int maxExtra = 5, long meret = 5000, Gender hairGender = Gender.All) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            BeautyStyleDefaultSlotCount = baseSlots,
            BeautyStyleMaxSlotCount = maxExtra,
            BeautyStyleExpandSlotCount1time = 1,
            BeautyStyleExpandSlotPrice = (int) Price,
        });
        hair = test.Store(TestGameSession.Metadata(HairId, EquipSlot.HR, hairGender), 1, ItemGroup.Outfit, EquipSlot.HR);
        test.Register(TestGameSession.Metadata(OtherHairId, EquipSlot.HR));
        test.Start();
        test.EnterMap(TestGameSession.Map(2000062));
        session.Beauty = new BeautyManager(session);
        Open(type);
        session.Currency.Meso = 5000;
        session.Currency.Meret = meret;
        test.TakeSent();
    }

    // The style save npc's shop (KR 510: a hair shop row with a 1000 meso style fee), opened as type.
    private void Open(BeautyShopType type) {
        var style = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        var color = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 0, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(510, BeautyShopCategory.Hair, 0, style, color, false, false, 0, 0,
            ItemTag.None, [], []), []) {
            Type = type,
        };
    }

    private void Send(byte mode, Action<IByteWriter>? payload = null) {
        new BeautyHandler {
            ItemMetadata = session.ItemMetadata,
            TableMetadata = test.TableMetadata,
            NpcMetadata = null!,
            ServerTableMetadata = test.ServerTableMetadata,
        }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(mode);
            payload?.Invoke(writer);
        }));
    }

    private void Save(long uid) => Send(16, w => w.WriteLong(uid));
    private void SaveExtending(long uid) => Send(17, w => w.WriteLong(uid));
    private void Delete(long uid) => Send(18, w => { w.WriteLong(uid); w.WriteByte(0); });
    private void Extend() => Send(19);
    private void Apply(long uid) => Send(21, w => { w.WriteLong(uid); w.WriteByte(0); });

    private bool SentError(BeautyError error) {
        byte[] expected = BeautyPacket.Error(error).ToArray();
        return test.TakeSent().Any(packet => packet.SequenceEqual(expected));
    }

    [TestCase(BeautyShopType.Default)]
    [TestCase(BeautyShopType.Modify)]
    [TestCase(BeautyShopType.Random)]
    public void OutsideAStyleSaveShop_NothingIsSavedBoughtOrApplied(BeautyShopType type) {
        Start(type, baseSlots: 1);

        Save(hair.Uid);
        SaveExtending(hair.Uid);
        Extend();

        Assert.That(session.Beauty.SavedCount, Is.Zero);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.Zero);
    }

    [Test]
    public void Save_TheWornHair_IsListed() {
        Start(BeautyShopType.Save);

        Save(hair.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
        Assert.That(session.Beauty.OpenSlots, Is.EqualTo((short) 29));
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(hair.Uid));
    }

    // The client sends the worn hair's uid at each save and does not test for a style already saved; each save is
    // its own copy and slot.
    [Test]
    public void Save_TheSameHairTwice_TakesTwoSlots() {
        Start(BeautyShopType.Save);

        Save(hair.Uid);
        Save(hair.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(2));
        Assert.That(session.Beauty.OpenSlots, Is.EqualTo((short) 28));
    }

    [Test]
    public void Save_IntoAFullList_IsRefusedWithTheSlotMaxError() {
        Start(BeautyShopType.Save, baseSlots: 1);
        Save(hair.Uid);
        Item other = Wear();
        test.TakeSent();

        Save(other.Uid);

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_slot_max), Is.True);
        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
    }

    [Test]
    public void SaveExtending_IntoAFullList_BuysOneSlotAndSaves() {
        Start(BeautyShopType.Save, baseSlots: 1);
        Save(hair.Uid);
        Item other = Wear();

        SaveExtending(other.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(2));
        Assert.That(session.Beauty.SlotCount, Is.EqualTo(2));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.EqualTo((short) 1));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L - Price));
    }

    // A sale of the style shop (event 7001, 30 % off; pcRoom: the PC room row, no sale for others), then the other hair
    // bought there at its row fee of 1000 meso and the shop turned into the style save list.
    private Item BuyHairInASale(bool pcRoom = false) {
        test.AddEvent(7001, GameEventType.SaleBeautyShop, new SaleBeautyShop(510, 3000, pcRoom));
        var fee = new BeautyShopCostMetadata(ShopCurrencyType.Meso, 1000, string.Empty, 0);
        session.BeautyShop = new BeautyShop(new BeautyShopMetadata(510, BeautyShopCategory.Hair, 0, fee, fee, false, false, 0, 0,
            ItemTag.None, [], []), [new BeautyShopItem(OtherHairId, fee, 1, 0, 0, 0, default)]);
        Send(3, writer => {
            writer.WriteByte(0);
            writer.WriteBool(false);
            writer.WriteInt(OtherHairId);
            writer.WriteLong(0);
            writer.WriteClass<HairAppearance>(new HairAppearance(default));
        });
        Item bought = session.Item.Equips.Get(EquipSlot.HR)!;
        Assert.That((bought.Id, session.Currency.Meso), Is.EqualTo((OtherHairId, pcRoom ? 4000L : 4300L)));
        Open(BeautyShopType.Save);
        test.TakeSent();
        return bought;
    }

    // "Discounted hair cannot be stored during the beauty day event": error 25, which only the style dialog answers
    // (uibeautyshopstyledialog.cpp:1347); no slot is taken or bought.
    [Test]
    public void Save_AHairBoughtInARunningSale_IsRefusedWithError25() {
        Start(BeautyShopType.Save, baseSlots: 0);
        Item bought = BuyHairInASale();

        Save(bought.Uid);
        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_save_cant_not_beauty_day), Is.True);
        SaveExtending(bought.Uid);

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_save_cant_not_beauty_day), Is.True);
        Assert.That((session.Beauty.SavedCount, session.Currency.Meret), Is.EqualTo((0, 5000L)));
    }

    [Test]
    public void Save_AHairBoughtInASale_IsListedOnceTheSaleIsOver() {
        Start(BeautyShopType.Save);
        Item bought = BuyHairInASale();
        test.AddEvent(7001, GameEventType.SaleBeautyShop, new SaleBeautyShop(510, 3000, false), DateTime.Now.AddDays(-3), DateTime.Now.AddDays(-1));

        Save(bought.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
    }

    // Only the sale of the shop the hair was bought in refuses its save: another shop's sale does not.
    [Test]
    public void Save_AHairBoughtInASale_IsListedWhileOnlyAnotherShopsSaleRuns() {
        Start(BeautyShopType.Save);
        Item bought = BuyHairInASale();
        test.AddEvent(7001, GameEventType.SaleBeautyShop, new SaleBeautyShop(511, 3000, false));

        Save(bought.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
    }

    // A PC room row lowers no fee: the hair was bought at its full fee.
    [Test]
    public void Save_AHairBoughtAtTheFullFee_IsListedDuringASale() {
        Start(BeautyShopType.Save);
        Item bought = BuyHairInASale(pcRoom: true);

        Save(bought.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
    }

    // The save that bought the extension is answered with 17, the plain save with 16 (uibeautyshopdialog.cpp:1577).
    [TestCase(1, (byte) 17)]
    [TestCase(30, (byte) 16)]
    public void SaveExtending_AnswersWithTheCommandOfWhatItDid(int baseSlots, byte command) {
        Start(BeautyShopType.Save, baseSlots: baseSlots);
        Save(hair.Uid);
        Item other = Wear();
        test.TakeSent();

        SaveExtending(other.Uid);

        byte[] head = BeautyPacket.Error(BeautyError.s_beauty_msg_error_style_slot_max).ToArray()[..2];
        Assert.That(test.TakeSent().Count(packet => packet.Length > 2 && packet[..2].SequenceEqual(head) && packet[2] == command), Is.EqualTo(1));
    }

    // With an open slot the extension is not bought.
    [Test]
    public void SaveExtending_WithAnOpenSlot_SavesWithoutBuying() {
        Start(BeautyShopType.Save);

        SaveExtending(hair.Uid);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.Zero);
    }

    [Test]
    public void SaveExtending_WithoutTheMerets_SavesNothing() {
        Start(BeautyShopType.Save, baseSlots: 1, meret: Price - 1);
        Save(hair.Uid);
        Item other = Wear();
        test.TakeSent();

        SaveExtending(other.Uid);

        Assert.That(SentError(BeautyError.s_err_lack_merat_ask), Is.True);
        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
        Assert.That(session.Currency.Meret, Is.EqualTo(Price - 1));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.Zero);
    }

    // A uid that is not worn hair buys nothing.
    [Test]
    public void SaveExtending_AnUnwornUid_BuysNothing() {
        Start(BeautyShopType.Save, baseSlots: 1);
        Save(hair.Uid);

        SaveExtending(hair.Uid + 1000);

        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.Zero);
    }

    [Test]
    public void Extend_BuysOneSlotEachTimeUpToTheMost() {
        Start(BeautyShopType.Save, maxExtra: 2);

        Extend();
        Extend();
        test.TakeSent();
        Extend();

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_slot_extend_max), Is.True);
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.EqualTo((short) 2));
        Assert.That(session.Beauty.SlotCount, Is.EqualTo(32));
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L - 2 * Price));
    }

    // The KR client's constants have no extra slots (BeautyStyleMaxSlotCount 0): it never sends 17 or 19, and the
    // server sells none either.
    [Test]
    public void Extend_WithNoExtraSlots_IsAtMax() {
        Start(BeautyShopType.Save, maxExtra: 0);

        Extend();

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_slot_extend_max), Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
    }

    // The bought slot and its merets are stored in one save.
    [Test]
    public void Extend_StoresTheSlotWithItsMerets() {
        Start(BeautyShopType.Save);

        Extend();

        using var context = new Ms2Context(test.Options);
        Assert.That(context.CharacterUnlock.AsNoTracking().Single(unlock => unlock.CharacterId == test.CharacterId).HairSlotExpand,
            Is.EqualTo((short) 1));
        Assert.That(context.Account.AsNoTracking().Single(account => account.Id == test.AccountId).Currency.Meret, Is.EqualTo(5000L - Price));
    }

    // A refused save keeps the merets and sells no slot.
    [Test]
    public void Extend_WhoseSaveFails_KeepsTheMerets() {
        Start(BeautyShopType.Save);
        test.FailSave = _ => true;

        Extend();

        test.FailSave = null;
        Assert.That(SentError(BeautyError.s_beauty_msg_error_code), Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000L));
        Assert.That(session.Player.Value.Unlock.HairSlotExpand, Is.Zero);
        Assert.That(session.Beauty.SlotCount, Is.EqualTo(30));
    }

    [Test]
    public void Delete_ASavedStyle_FreesItsSlot() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();

        Delete(saved);

        Assert.That(session.Beauty.SavedCount, Is.Zero);
        Assert.That(session.Beauty.OpenSlots, Is.EqualTo((short) 30));
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(hair.Uid));
    }

    [Test]
    public void Delete_OutsideAStyleSaveShop_KeepsTheStyle() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Open(BeautyShopType.Default);

        Delete(saved);

        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
    }

    [Test]
    public void Apply_ASavedStyle_PutsItOnForTheStyleFee() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Item other = Wear();

        Apply(saved);

        Item? worn = session.Item.Equips.Get(EquipSlot.HR);
        Assert.That(worn?.Id, Is.EqualTo(HairId));
        Assert.That(worn?.Uid, Is.Not.EqualTo(other.Uid).And.Not.EqualTo(saved));
        Assert.That(session.Currency.Meso, Is.EqualTo(4000L));
        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
    }

    // The style's copy and its fee are stored in one save.
    [Test]
    public void Apply_StoresTheCopyWithItsFee() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Wear();

        Apply(saved);

        long copy = session.Item.Equips.Get(EquipSlot.HR)!.Uid;
        using var context = new Ms2Context(test.Options);
        Assert.That(context.Character.AsNoTracking().Single(character => character.Id == test.CharacterId).Currency.Meso, Is.EqualTo(4000L));
        Assert.That(context.Item.AsNoTracking().Any(item => item.Id == copy && item.ItemId == HairId), Is.True);
    }

    // A refused save keeps the fee and the worn hair.
    [Test]
    public void Apply_WhoseSaveFails_KeepsTheFeeAndTheWornHair() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Item other = Wear();
        test.FailSave = _ => true;

        Apply(saved);

        test.FailSave = null;
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(other.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    [Test]
    public void Apply_AUidThatIsNotSaved_TakesNoFee() {
        Start(BeautyShopType.Save);
        Item other = Wear();

        Apply(hair.Uid);

        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(other.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    [Test]
    public void Apply_WithoutTheFee_KeepsTheWornHair() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Item other = Wear();
        session.Currency.Meso = 999;

        Apply(saved);

        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(other.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(999L));
    }

    [Test]
    public void Apply_OutsideAStyleSaveShop_TakesNoFee() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        Item other = Wear();
        Open(BeautyShopType.Modify);

        Apply(saved);

        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(other.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    // A style of the other gender is refused with 23 and takes no fee (uibeautyshopstyledialog.cpp:1354).
    [Test]
    public void Apply_AStyleOfTheOtherGender_IsRefusedWithError23() {
        Start(BeautyShopType.Save, hairGender: Gender.Female);
        Save(hair.Uid);
        long saved = SavedUid();
        Item other = Wear();
        test.TakeSent();

        Apply(saved);

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_disable_gender), Is.True);
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(other.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    // The style already worn is refused with 24 and takes no fee (uibeautyshopstyledialog.cpp:1345).
    [Test]
    public void Apply_TheWornStyle_IsRefusedWithError24() {
        Start(BeautyShopType.Save);
        Save(hair.Uid);
        long saved = SavedUid();
        test.TakeSent();

        Apply(saved);

        Assert.That(SentError(BeautyError.s_beauty_msg_error_style_apply_already), Is.True);
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(hair.Uid));
        Assert.That(session.Currency.Meso, Is.EqualTo(5000L));
    }

    // A second hair, worn in place of the first.
    private Item Wear() {
        Assert.That(session.ItemMetadata.TryGet(OtherHairId, out ItemMetadata? metadata), Is.True);
        Item item;
        using (GameStorage.Request db = session.GameStorage.Context()) {
            item = db.CreateItem(session.CharacterId, new Item(metadata!))!;
        }
        Assert.That(session.Item.Equips.EquipCosmetic(item, EquipSlot.HR), Is.True);
        Assert.That(session.Item.Equips.Get(EquipSlot.HR)?.Uid, Is.EqualTo(item.Uid));
        return item;
    }

    // The uid of the one saved style, read from the SaveHair answer's copy.
    private long SavedUid() {
        Assert.That(session.Beauty.SavedCount, Is.EqualTo(1));
        long uid = test.Rows().Where(row => row.Group == ItemGroup.SavedHair).Select(row => row.Id).Single();
        Assert.That(session.Beauty.HasSavedCosmetic(uid), Is.True);
        return uid;
    }
}
