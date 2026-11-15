using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Database.Extensions;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// ItemEquipHandler.Handle in the order KMS2 sends its answers, and the gear score that follows a gear change.
public class ItemEquipHandlerTests {
    private TestGameSession test = null!;
    private readonly ItemEquipHandler handler = new();

    private void Equip(GameSession session, Item item, EquipSlot slot) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(item.Uid);
            writer.WriteByte((byte) slot);
            writer.WriteByte(0);
        }));
    }

    private void Unequip(GameSession session, Item item) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(item.Uid);
        }));
    }

    private static byte Mode(byte[] packet) => packet[2];

    // An item that is taken off: ItemPutOff, then the bag's add.
    [Test]
    public void Unequip_SendsPutOffBeforeTheBagAdd() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Equip(session, cap, EquipSlot.CP);
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.ItemPutOn }));

        Unequip(session, cap);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemPutOff, SendOp.ItemInventory }));
        Assert.That(Mode(sent[1]), Is.EqualTo(0));
        Assert.That(session.Item.Inventory.Get(cap.Uid), Is.Not.Null);
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Null);
    }

    // Hair, a face, face decoration and ears do not come off (CItem::HasAnyEquipSlotOf, slots 1/2/3/18, refused by the
    // client with notice 207): an unequip for one is dropped and the item stays worn, with room in the bag. It was
    // discarded (UnequipInternal gives these slots up). A cap still comes off into the bag.
    [TestCase(EquipSlot.HR, 10200001, false)]
    [TestCase(EquipSlot.FA, 10300001, false)]
    [TestCase(EquipSlot.FD, 10400001, false)]
    [TestCase(EquipSlot.ER, 10500001, false)]
    [TestCase(EquipSlot.CP, 11300001, true)]
    public void Unequip_OfABeautySlotIsDropped(EquipSlot slot, int itemId, bool comesOff) {
        test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(itemId, slot), 1, ItemGroup.Outfit, slot);
        GameSession session = test.Start();
        Assert.That(session.Item.Equips.Get(item.Uid), Is.Not.Null);
        test.TakeSent();

        Unequip(session, item);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(item.Uid) == null, Is.EqualTo(comesOff));
            Assert.That(session.Item.Inventory.Get(item.Uid) != null, Is.EqualTo(comesOff));
            Assert.That(test.TakeSent().Count == 0, Is.EqualTo(!comesOff));
        });
    }

    // A worn item taken off with a full bag stays worn and gets the bag's notice; an unknown uid is dropped. Before,
    // the item left its slot before the bag was checked (lost from both), and either request cut the session.
    [Test]
    public void Unequip_WithAFullBagKeepsTheItemWorn() {
        test = new TestGameSession();
        Item cap = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, type: 1, slotMax: 1));
        GameSession session = test.Start();
        Equip(session, cap, EquipSlot.CP);
        while (session.Item.Inventory.FreeSlots(InventoryType.Gear) > 0) {
            Assert.That(session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(11300100, EquipSlot.CP, type: 1, slotMax: 1))), Is.True);
        }
        test.TakeSent();

        Unequip(session, cap);
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(1);
            writer.WriteLong(cap.Uid + 1000);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.Get(cap.Uid), Is.Null);
            Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
                Convert.ToHexString(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory).ToArray()),
            }));
        });
    }

    // An item that takes the place of a worn one: the new item leaves the bag, the old one enters it, then
    // ItemPutOff (old) and ItemPutOn (new).
    [Test]
    public void Equip_OverAWornItemAddsItToTheBagFirst() {
        test = new TestGameSession();
        Item first = test.Store(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1));
        Item second = test.Store(TestGameSession.Metadata(11300002, EquipSlot.CP, slotMax: 1));
        GameSession session = test.Start();
        Equip(session, first, EquipSlot.CP);
        test.TakeSent();

        Equip(session, second, EquipSlot.CP);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] {
            SendOp.ItemInventory, SendOp.ItemInventory, SendOp.ItemPutOff, SendOp.ItemPutOn,
        }));
        Assert.That(sent.Take(2).Select(Mode), Is.EqualTo(new byte[] { 1, 0 }));
        Assert.That(session.Item.Equips.Get(second.Uid), Is.Not.Null);
        Assert.That(session.Item.Inventory.Get(first.Uid), Is.Not.Null);
    }

    private static ItemMetadata Bow(int id) {
        return TestGameSession.Metadata(id, EquipSlot.RH, type: 1, slotMax: 1) with { SlotNames = [EquipSlot.RH, EquipSlot.LH] };
    }

    // A two-handed weapon over a worn two-handed weapon with a full bag: the worn one takes the new one's bag slot.
    [TestCase(48)]
    [TestCase(47)]
    public void Equip_TwoHandedOverTwoHandedNeedsNoFreeSlot(int held) {
        test = new TestGameSession();
        Item worn = test.Store(Bow(15100304));
        Item next = test.Store(Bow(15100305));
        GameSession session = test.Start();
        Equip(session, worn, EquipSlot.RH);
        for (int i = 0; i < held - 1; i++) {
            Assert.That(session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(11300001 + i, EquipSlot.CP, type: 1, slotMax: 1))), Is.True);
        }
        Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(48 - held));
        short slot = session.Item.Inventory.Get(next.Uid)!.Slot;
        test.TakeSent();

        Equip(session, next, EquipSlot.RH);

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(next.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.Get(worn.Uid)?.Slot, Is.EqualTo(slot));
            Assert.That(session.Item.Inventory.FreeSlots(InventoryType.Gear), Is.EqualTo(48 - held));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // A two-handed weapon over a weapon and a shield: two items come off, so one slot beyond the new item's own
    // must be free. With a full bag the equip is refused with the bag's own notice (ItemInventory error 0).
    [TestCase(48, false)]
    [TestCase(47, true)]
    public void Equip_TwoHandedOverTwoItemsNeedsOneFreeSlot(int held, bool equipped) {
        test = new TestGameSession();
        Item sword = test.Store(TestGameSession.Metadata(13200001, EquipSlot.RH, type: 1, slotMax: 1));
        Item shield = test.Store(TestGameSession.Metadata(14100001, EquipSlot.LH, type: 1, slotMax: 1));
        Item bow = test.Store(Bow(15100304));
        GameSession session = test.Start();
        Equip(session, sword, EquipSlot.RH);
        Equip(session, shield, EquipSlot.LH);
        for (int i = 0; i < held - 1; i++) {
            Assert.That(session.Item.Inventory.Add(test.Store(TestGameSession.Metadata(11300001 + i, EquipSlot.CP, type: 1, slotMax: 1))), Is.True);
        }
        test.TakeSent();

        Equip(session, bow, EquipSlot.RH);

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(bow.Uid) != null, Is.EqualTo(equipped));
            Assert.That(session.Item.Equips.Get(sword.Uid) == null, Is.EqualTo(equipped));
            Assert.That(session.Item.Equips.Get(shield.Uid) == null, Is.EqualTo(equipped));
            if (!equipped) {
                Assert.That(sent.Select(Convert.ToHexString), Is.EqualTo(new[] {
                    Convert.ToHexString(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory).ToArray()),
                }));
            }
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // Guild mode 14, as KMS2 sends it: the member's name and the gear score.
    [Test]
    public void GuildGearScorePacket() {
        Assert.That(Convert.ToHexString(GuildPacket.UpdateMemberGearScore("Abcde", 187399).ToArray()),
            Is.EqualTo("8A00" + "0E" + "0500" + "41006200630064006500" + "07DC0200"));
    }

    // The gear score goes to the world once per change.
    [Test]
    public void GearScore_GoesToTheWorldWhenItChanges() {
        test = new TestGameSession();
        GameSession session = test.Start();
        test.World.PlayerUpdates.Clear();

        session.Stats.SendGearScore(session, 3382);
        session.Stats.SendGearScore(session, 3382);
        session.Stats.SendGearScore(session, 3400);

        Assert.That(test.World.PlayerUpdates.Select(update => (update.CharacterId, update.HasGearScore, update.GearScore)), Is.EqualTo(new[] {
            (session.CharacterId, true, 3382), (session.CharacterId, true, 3400),
        }));
    }

    // Kind 2 (the second outfit tab) while the tab's period runs: it lands there only with Skin2tab on; with it off the
    // client has no such tab and the item goes to the first outfit tab.
    [TestCase(true, ExpectedResult = "Outfit2")]
    [TestCase(false, ExpectedResult = "Outfit")]
    public string Outfit2_NeedsSkin2tab(bool on) {
        test = new TestGameSession();
        ItemMetadata metadata = TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1);
        Item cap = test.Store(metadata with { Property = metadata.Property with { IsSkin = true } });
        GameSession session = test.Start();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Player.Value.Character.SkinTabStart = now - 60;
        session.Player.Value.Character.SkinTabEnd = now + 3600;
        if (!on) {
            test.SetFeaturesOff(Maple2.Server.Game.Util.EquipKind.Outfit2Feature);
        }

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(cap.Uid);
            writer.WriteByte((byte) EquipSlot.CP);
            writer.WriteByte(2);
        }));

        return session.Item.Equips.Outfit2.ContainsKey(EquipSlot.CP) ? "Outfit2"
            : session.Item.Equips.Outfit.ContainsKey(EquipSlot.CP) ? "Outfit" : "none";
    }

    // The client's equip checks (RequestEquipItem, inventory.cpp:1242) refuse an item of the other gender, a
    // premium-club item without premium club, a wedding item unless married, an item of another job and an expired one
    // (a maid furnishing excepted); the server refuses such a packet too, the item stays in the bag and nothing
    // is worn. Before, the gender, premium-club and wedding items were worn, and a beginner's or an all-jobs list item
    // was refused.
    private TestGameSession Start(ItemMetadata metadata, out Item item, Action<GameSession>? setup = null) {
        test = new TestGameSession(gender: Gender.Male);
        item = test.Store(metadata);
        GameSession session = test.Start();
        session.Player.Value.Account.PremiumTime = 0;
        setup?.Invoke(session);
        test.TakeSent();
        return test;
    }

    private string PutOn(Func<ItemMetadataLimit, ItemMetadataLimit> limit, Action<GameSession>? setup = null) {
        ItemMetadata metadata = TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1);
        Start(metadata with { Limit = limit(metadata.Limit) }, out Item cap, setup);
        GameSession session = test.Session;

        Equip(session, cap, EquipSlot.CP);

        bool worn = session.Item.Equips.Get(cap.Uid) != null;
        Assert.That(session.Item.Inventory.Get(cap.Uid) != null, Is.Not.EqualTo(worn));
        return worn ? "worn" : "refused";
    }

    [TestCase(Gender.Male, ExpectedResult = "worn")]
    [TestCase(Gender.All, ExpectedResult = "worn")]
    [TestCase(Gender.Female, ExpectedResult = "refused")]
    public string PutOn_TheOtherGendersItemIsRefused(Gender gender) => PutOn(limit => limit with { Gender = gender });

    [TestCase(false, ExpectedResult = "refused")]
    [TestCase(true, ExpectedResult = "worn")]
    public string PutOn_APremiumClubItemNeedsPremiumClub(bool premium) => PutOn(limit => limit with { RequireVip = true },
        session => session.Player.Value.Account.PremiumTime = premium ? DateTime.Now.AddDays(1).ToEpochSeconds() : 0);

    [TestCase(MaritalStatus.Single, ExpectedResult = "refused")]
    [TestCase(MaritalStatus.Engaged, ExpectedResult = "refused")]
    [TestCase(MaritalStatus.Married, ExpectedResult = "worn")]
    [TestCase(MaritalStatus.DivorceFiledByGroom, ExpectedResult = "worn")]
    [TestCase(MaritalStatus.DivorceFiledByBride, ExpectedResult = "worn")]
    [TestCase(MaritalStatus.Divorced, ExpectedResult = "refused")]
    public string PutOn_AWeddingItemNeedsAMarriage(MaritalStatus status) => PutOn(limit => limit with { RequireWedding = true },
        session => session.Marriage.Marriage.Status = status);

    // ItemLimit::IsJobAllowed (itemdatastruct.cpp:690): a beginner counts as a knight, a list naming job 0 or the
    // beginner allows every job, an empty list too.
    [TestCase(Job.Newbie, new[] { JobCode.Knight }, ExpectedResult = "worn")]
    [TestCase(Job.Newbie, new[] { JobCode.Wizard }, ExpectedResult = "refused")]
    [TestCase(Job.Wizard, new[] { JobCode.Newbie }, ExpectedResult = "worn")]
    [TestCase(Job.Wizard, new[] { JobCode.None }, ExpectedResult = "worn")]
    [TestCase(Job.Wizard, new[] { JobCode.Knight, JobCode.Wizard }, ExpectedResult = "worn")]
    [TestCase(Job.Wizard, new[] { JobCode.Knight }, ExpectedResult = "refused")]
    [TestCase(Job.WizardII, new JobCode[0], ExpectedResult = "worn")]
    public string PutOn_TheJobAsTheClientTestsIt(Job job, JobCode[] limits) => PutOn(limit => limit with { JobLimits = limits },
        session => session.Player.Value.Character.Job = job);

    // An expired furnishing that places a maid stays usable (CItem::FUN_140528980); another expired item is refused.
    [TestCase(0, ExpectedResult = "s_item_err_puton_expired")]
    [TestCase(1, ExpectedResult = "s_empty_string")]
    public string PutOn_AnExpiredMaidItemIsAllowed(int maidId) {
        ItemMetadata metadata = TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1);
        if (maidId > 0) {
            metadata = metadata with { Housing = new ItemMetadataHousing(0, 0, 0, default, false, MaidId: maidId) };
        }
        Start(metadata, out Item cap);
        Item held = test.Session.Item.Inventory.Get(cap.Uid)!;
        held.ExpiryTime = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        return EquipManager.ValidateEquipItem(test.Session, held).ToString();
    }

    // The client's order (GetItemUseError, then GetItemRequirementError, inventory.cpp:1578-1601): an item that fails
    // several checks gets the first one's answer (expired, PC bang, gender, job, level, premium club).
    [Test]
    public void PutOn_TheFirstFailedCheckAnswers() {
        ItemMetadata metadata = TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1);
        Start(metadata with { Limit = metadata.Limit with {
            Level = 99, Gender = Gender.Female, JobLimits = [JobCode.Wizard], RequireVip = true, RequirePcBang = true,
        } }, out Item cap, session => session.Player.Value.Character.Job = Job.Knight);
        Item held = test.Session.Item.Inventory.Get(cap.Uid)!;
        var answers = new List<StringCode>();
        Func<ItemMetadataLimit, ItemMetadataLimit>[] steps = [
            limit => limit with { RequirePcBang = false },
            limit => limit with { Gender = Gender.All },
            limit => limit with { JobLimits = [] },
            limit => limit with { Level = 0 },
            limit => limit with { RequireVip = false },
        ];
        held.ExpiryTime = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        answers.Add(EquipManager.ValidateEquipItem(test.Session, held));
        held.ExpiryTime = 0;
        Item current = held;
        foreach (Func<ItemMetadataLimit, ItemMetadataLimit> step in steps) {
            answers.Add(EquipManager.ValidateEquipItem(test.Session, current));
            current = new Item(current.Metadata with { Limit = step(current.Metadata.Limit) }) { Uid = current.Uid };
        }
        answers.Add(EquipManager.ValidateEquipItem(test.Session, current));
        Assert.That(answers, Is.EqualTo(new[] {
            StringCode.s_item_err_puton_expired, StringCode.s_item_err_puton_pcbang, StringCode.s_err_gender,
            StringCode.s_item_err_puton_job, StringCode.s_item_err_puton_low_level, StringCode.s_item_err_puton,
            StringCode.s_empty_string,
        }));
    }

    // A dead player puts nothing on and takes nothing off (Unk_Inventory::RequestEquipItem / RequestUnequipItem send
    // nothing while dead); a badge neither (EquipGem / UnequipGem). Before, all four went through.
    [Test]
    public void WhileDead_NothingGoesOnOrComesOff() {
        Start(TestGameSession.Metadata(11300001, EquipSlot.CP, slotMax: 1), out Item cap);
        GameSession session = test.Session;
        Item badge = test.Store(TestGameSession.Metadata(70400001, type: 7));
        Assert.That(session.Item.Inventory.Add(badge), Is.True);
        Equip(session, cap, EquipSlot.CP);
        Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
        Item other = test.Store(TestGameSession.Metadata(11300002, EquipSlot.CP, slotMax: 1));
        Assert.That(session.Item.Inventory.Add(other), Is.True);
        typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("isDead", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session.Player, true);
        test.TakeSent();
        var badges = new BadgeEquipHandler();

        Equip(session, other, EquipSlot.CP);
        Unequip(session, cap);
        badges.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(badge.Uid);
            writer.WriteByte((byte) BadgeType.Damage);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Item.Equips.Get(cap.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.Get(other.Uid), Is.Not.Null);
            Assert.That(session.Item.Inventory.Get(badge.Uid), Is.Not.Null);
            Assert.That(session.Item.Equips.Get(BadgeType.Damage), Is.Null);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    // Badge equip runs the same put-on checks: a badge of the other gender stays in the bag.
    [TestCase(Gender.Male, ExpectedResult = true)]
    [TestCase(Gender.Female, ExpectedResult = false)]
    public bool Badge_TheOtherGendersBadgeIsRefused(Gender gender) {
        ItemMetadata metadata = TestGameSession.Metadata(70400001, type: 7);
        Start(metadata with { Limit = metadata.Limit with { Gender = gender } }, out Item badge);
        new BadgeEquipHandler().Handle(test.Session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteLong(badge.Uid);
            writer.WriteByte((byte) BadgeType.Damage);
        }));
        return test.Session.Item.Equips.Get(BadgeType.Damage)?.Uid == badge.Uid;
    }
}
