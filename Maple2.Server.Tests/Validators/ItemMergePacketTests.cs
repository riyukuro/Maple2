using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class ItemMergePacketTests {
    // ItemMerge 0x003F mode 0 has no payload; the answer the client reads is u8 mode 0, u8 0.
    [Test]
    public void InfoAnswerIsModeZeroAndNothingPending() {
        byte[] bytes = ItemMergePacket.NoPendingChoice().ToArray();
        Assert.That(bytes.Length, Is.EqualTo(4));
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 0 with a waiting result: u8 1, int item id, long item uid, then the three ints of mode 4.
    [Test]
    public void InfoAnswerWithPendingChoice() {
        byte[] bytes = ItemMergePacket.PendingChoice(11200001, 77L, new MergeRoll(BasicAttribute.Dexterity, SpecialAttribute.None, 12)).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(1));
        Assert.That(packet.ReadInt(), Is.EqualTo(11200001));
        Assert.That(packet.ReadLong(), Is.EqualTo(77L));
        Assert.That(packet.ReadInt(), Is.EqualTo((int) BasicAttribute.Dexterity));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(12));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 0, u8 2: only the item id follows.
    [Test]
    public void InfoAnswerWithMissingItem() {
        byte[] bytes = ItemMergePacket.PendingItemMissing(11200001).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadInt(), Is.EqualTo(11200001));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    // Mode 4: basic attribute or -1, special attribute, value.
    [Test]
    public void ChoiceOfSpecialOptionHasNoBasicAttribute() {
        byte[] bytes = ItemMergePacket.Choice(new MergeRoll(null, SpecialAttribute.Experience, 35)).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(4));
        Assert.That(packet.ReadInt(), Is.EqualTo(-1));
        Assert.That(packet.ReadInt(), Is.EqualTo((int) SpecialAttribute.Experience));
        Assert.That(packet.ReadInt(), Is.EqualTo(35));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    private static ItemMergeTable.Option RateOption() {
        // A rate attribute: the steps are in Rates, Values stay 0.
        return new ItemMergeTable.Option(
            Values: [new ItemMergeTable.Range<int>(0, 0), new ItemMergeTable.Range<int>(0, 0)],
            Rates: [new ItemMergeTable.Range<int>(11, 20), new ItemMergeTable.Range<int>(21, 30)],
            Weights: [1, 1]);
    }

    [Test]
    public void BoundsCoverTheStepsOfARateOption() {
        Assert.That(RateOption().Bounds(), Is.EqualTo(new ItemMergeTable.Range<int>(11, 30)));
        Assert.That(new ItemMergeTable.Option([], [], []).Bounds(), Is.EqualTo(new ItemMergeTable.Range<int>(0, 0)));
    }

    // The preview (mode 2) carries the range the client shows: not 0 to 0 for a rate option.
    [Test]
    public void SelectWritesTheRangeOfARateOption() {
        var entry = new ItemMergeTable.Entry(1, 100, [],
            new Dictionary<BasicAttribute, ItemMergeTable.Option> { [BasicAttribute.Piercing] = RateOption() },
            new Dictionary<SpecialAttribute, ItemMergeTable.Option>());
        byte[] bytes = ItemMergePacket.Select(entry, 2).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadLong(), Is.EqualTo(200));
        Assert.That(packet.ReadByte(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.ReadInt(), Is.EqualTo(1));
        Assert.That(packet.ReadShort(), Is.EqualTo((short) BasicAttribute.Piercing));
        Assert.That(packet.ReadInt(), Is.EqualTo(11));
        Assert.That(packet.ReadInt(), Is.EqualTo(30));
        Assert.That(packet.ReadInt(), Is.EqualTo(0));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [Test]
    public void RollStaysInsideTheStepsAndReachesBothEnds() {
        var entry = new ItemMergeTable.Entry(1, 100, [],
            new Dictionary<BasicAttribute, ItemMergeTable.Option> { [BasicAttribute.Piercing] = RateOption() },
            new Dictionary<SpecialAttribute, ItemMergeTable.Option>());
        var random = new Random(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 2000; i++) {
            MergeRoll roll = ItemMergeManager.Roll(entry, random);
            Assert.That(roll.Basic, Is.EqualTo(BasicAttribute.Piercing));
            Assert.That(roll.Value, Is.InRange(11, 30));
            seen.Add(roll.Value);
        }
        Assert.That(seen, Does.Contain(11));
        Assert.That(seen, Does.Contain(30));
    }

    // The merge option is the only one of its group and the crystal's id is the int after the groups.
    [Test]
    public void MergeOptionReplacesTheOldOneAndTravelsWithTheItemStats() {
        var stats = new ItemStats();
        Assert.That(stats.HasMergeOption, Is.False);
        stats.SetMergeOption(new MergeRoll(BasicAttribute.Dexterity, SpecialAttribute.None, 12), 37000001);
        stats.SetMergeOption(new MergeRoll(null, SpecialAttribute.Experience, 35), 37000002);
        Assert.That(stats.HasMergeOption, Is.True);
        Assert.That(stats[ItemStats.Type.Empowerment].Basic, Is.Empty);
        Assert.That(stats[ItemStats.Type.Empowerment].Special[SpecialAttribute.Experience].Value, Is.EqualTo(35));
        Assert.That(stats.Clone().MergeId, Is.EqualTo(37000002));

        var writer = new ByteWriter();
        stats.WriteTo(writer);
        byte[] bytes = writer.ToArray();
        Assert.That(BitConverter.ToInt32(bytes, bytes.Length - 4), Is.EqualTo(37000002));
        var read = new ItemStats();
        read.ReadFrom(new ByteReader(bytes));
        Assert.That(read.MergeId, Is.EqualTo(37000002));
        Assert.That(read[ItemStats.Type.Empowerment].Special[SpecialAttribute.Experience].Value, Is.EqualTo(35));
    }

    private static Item ItemOf(int id, int rarity, long uid, string? revertParameter = null, int type = 1, int subType = 13, EquipSlot[]? slots = null) {
        var property = new ItemMetadataProperty(false, 0, 100, type, subType, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        ItemMetadataFunction? function = revertParameter == null ? null : new ItemMetadataFunction(ItemFunction.ItemMergeRevert, "ItemMergeRevert", revertParameter, false);
        var metadata = new ItemMetadata(id, $"{id}", slots ?? [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, function, [], null, null, null, null);
        return new Item(metadata, rarity) { Uid = uid };
    }

    private static Item Merged(int id, int rarity, int type = 1, int subType = 13, EquipSlot[]? slots = null) {
        Item item = ItemOf(id, rarity, 5, null, type, subType, slots);
        item.Stats = new ItemStats();
        item.Stats.SetMergeOption(new MergeRoll(BasicAttribute.Dexterity, SpecialAttribute.None, 12), 37000001);
        return item;
    }

    // The client writes the revert item's uid before the item's (uiitemmergerevertdialog.cpp:156).
    [Test]
    public void RevertRequestHasTheRevertItemFirst() {
        var writer = new ByteWriter();
        writer.WriteLong(111);
        writer.WriteLong(222);
        (long revertItemUid, long itemUid) = ItemMergeHandler.ReadRevert(new ByteReader(writer.ToArray()));
        Assert.That(revertItemUid, Is.EqualTo(111));
        Assert.That(itemUid, Is.EqualTo(222));
    }

    // After the revert the Empowerment group is empty and the int after the groups is 0; the answer
    // (mode 6) is the item's uid and the item.
    [Test]
    public void RevertClearsTheGroupAndTheMergeId() {
        Item item = Merged(11300001, 4);
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(37000021, 4, 9, "1"), item), Is.EqualTo(ItemMergeError.ok));
        item.Stats!.ClearMergeOption();
        Assert.That(item.Stats.HasMergeOption, Is.False);
        Assert.That(item.Stats[ItemStats.Type.Empowerment].Count, Is.EqualTo(0));
        Assert.That(item.Stats.MergeId, Is.EqualTo(0));

        var writer = new ByteWriter();
        item.Stats.WriteTo(writer);
        byte[] stats = writer.ToArray();
        Assert.That(stats.Length, Is.EqualTo(5 * 4 + 4));
        Assert.That(stats, Is.All.EqualTo(0));

        byte[] bytes = ItemMergePacket.Remove(item).ToArray();
        var packet = new ByteReader(bytes, 2);
        Assert.That(packet.ReadByte(), Is.EqualTo(6));
        Assert.That(packet.ReadLong(), Is.EqualTo(5));
        Assert.That(packet.Available, Is.GreaterThan(0));
    }

    // The codes of the client's own check (UIItemMergeRevertDialog::CheckItem).
    [Test]
    public void RevertErrors() {
        Item revert = ItemOf(37000021, 4, 9, "1");
        Assert.That(ItemMergeManager.CheckRevert(revert, null), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_mergeitem));
        Assert.That(ItemMergeManager.CheckRevert(null, Merged(11300001, 4)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(30000001, 4, 9), Merged(11300001, 4)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
        Assert.That(ItemMergeManager.CheckRevert(revert, Merged(11300001, 5)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_revert_rank));
        Assert.That(ItemMergeManager.CheckRevert(revert, ItemOf(11300001, 4, 5)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_revert_option));
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(37000021, 4, 9, "0"), Merged(11300001, 4)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(37000021, 4, 9, ""), Merged(11300001, 4)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_merge_scroll));
        // Subtype 13 is kind 1, subtypes 22 and 50 are kind 2.
        Assert.That(ItemMergeManager.CheckRevert(revert, Merged(12200001, 4, 1, 22)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_revert_slot));
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(37000024, 4, 9, "2"), Merged(11300001, 4)), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_revert_slot));
        Assert.That(ItemMergeManager.CheckRevert(ItemOf(37000024, 4, 9, "2"), Merged(15000001, 4, 1, 50)), Is.EqualTo(ItemMergeError.ok));
        Assert.That(ItemMergePacket.Error(ItemMergeError.s_item_merge_option_error_invalid_revert_slot).ToArray()[2..], Is.EqualTo(new byte[] { 5, 8, 0 }));
    }

    // The kind and the price follow the item xml's property type and subtype (CItemData::GetEquipSubtype,
    // FUN_14053e410), not the digits of the id.
    [Test]
    public void RevertSlotAndPriceFollowTheEquipType() {
        static int Slot(int type, int subType, params EquipSlot[] slots) => ItemMergeManager.RevertSlot(ItemOf(11300001, 4, 5, null, type, subType, slots).Metadata);

        Assert.That(Slot(1, 13), Is.EqualTo(1));
        Assert.That(Slot(1, 34), Is.EqualTo(1));
        Assert.That(Slot(1, 22), Is.EqualTo(2));
        Assert.That(Slot(1, 49), Is.EqualTo(1));
        Assert.That(Slot(1, 50), Is.EqualTo(2));
        Assert.That(Slot(1, 56), Is.EqualTo(2));
        Assert.That(Slot(1, 57), Is.EqualTo(1));
        Assert.That(Slot(1, 80), Is.EqualTo(2));
        Assert.That(Slot(1, 83), Is.EqualTo(2));
        Assert.That(Slot(1, 84), Is.EqualTo(1));
        // The subtype counts only for property type 1.
        Assert.That(Slot(2, 22), Is.EqualTo(1));
        Assert.That(Slot(18, 50), Is.EqualTo(1));
        // An id whose digits say 22 or 50 has no say.
        Assert.That(ItemMergeManager.RevertSlot(ItemOf(12200001, 4, 5).Metadata), Is.EqualTo(1));
        Assert.That(ItemMergeManager.RevertSlot(ItemOf(11300001, 4, 5, null, 1, 50).Metadata), Is.EqualTo(2));

        Assert.That(ItemMergeManager.RevertPrice(Merged(11300001, 4)), Is.EqualTo(250000));
        Assert.That(ItemMergeManager.RevertPrice(Merged(11300001, 4, 1, 22)), Is.EqualTo(500000));
        Assert.That(ItemMergeManager.RevertPrice(Merged(12200001, 4, 2, 22)), Is.EqualTo(ItemMergeManager.RevertPrice(Merged(11300001, 4, 2, 0))));
        Assert.That(ItemMergeManager.EquipSubtype(ItemOf(12200001, 4, 5, null, 23, 22).Metadata), Is.EqualTo(0));
    }

    // Property type 23 is kind 2 only when the item's slots give both RH and LH; the first slot named OH
    // answers for both (CItemData::FUN_14053d460, ItemModelData::HasGroupType).
    [Test]
    public void RevertSlotOfPropertyType23FollowsTheSlots() {
        static int Slot(int type, params EquipSlot[] slots) => ItemMergeManager.RevertSlot(ItemOf(11300001, 4, 5, null, type, 0, slots).Metadata);

        Assert.That(Slot(23), Is.EqualTo(1));
        Assert.That(Slot(23, EquipSlot.RH), Is.EqualTo(1));
        Assert.That(Slot(23, EquipSlot.LH), Is.EqualTo(1));
        Assert.That(Slot(23, EquipSlot.RH, EquipSlot.LH), Is.EqualTo(2));
        Assert.That(Slot(23, EquipSlot.LH, EquipSlot.RH), Is.EqualTo(2));
        Assert.That(Slot(23, EquipSlot.OH), Is.EqualTo(2));
        // The walk stops at OH: a slot after it is not seen, one before it is.
        Assert.That(Slot(23, EquipSlot.CP, EquipSlot.OH), Is.EqualTo(2));
        Assert.That(Slot(23, EquipSlot.RH, EquipSlot.OH), Is.EqualTo(2));
        Assert.That(Slot(23, EquipSlot.CP, EquipSlot.CL), Is.EqualTo(1));
        // The slots count for property type 23 only.
        Assert.That(Slot(1, EquipSlot.RH, EquipSlot.LH), Is.EqualTo(1));
        Assert.That(Slot(22, EquipSlot.OH), Is.EqualTo(1));

        Item revert2 = ItemOf(37000024, 4, 9, "2");
        Assert.That(ItemMergeManager.CheckRevert(revert2, Merged(11300001, 4, 23, 0, [EquipSlot.OH])), Is.EqualTo(ItemMergeError.ok));
        Assert.That(ItemMergeManager.CheckRevert(revert2, Merged(11300001, 4, 23, 0, [EquipSlot.RH])), Is.EqualTo(ItemMergeError.s_item_merge_option_error_invalid_revert_slot));
    }
}
