using System;
using System.Linq;
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
    private void Start(BeautyShopType type, int baseSlots = 30, int maxExtra = 5, long meret = 5000) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            BeautyStyleDefaultSlotCount = baseSlots,
            BeautyStyleMaxSlotCount = maxExtra,
            BeautyStyleExpandSlotCount1time = 1,
            BeautyStyleExpandSlotPrice = (int) Price,
        });
        hair = test.Store(TestGameSession.Metadata(HairId, EquipSlot.HR), 1, ItemGroup.Outfit, EquipSlot.HR);
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
            ItemMetadata = session.ItemMetadata, TableMetadata = test.TableMetadata, NpcMetadata = null!,
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
