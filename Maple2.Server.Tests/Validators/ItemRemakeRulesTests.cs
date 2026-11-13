using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Validators;
using Maple2.Server.Game.Manager;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ItemRemakeRulesTests {
    // Mode 0 of 0x0075: i64 item, i64 ingredient, then the kept options.
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ChangeBytes(int count) {
        var writer = new ByteWriter();
        writer.WriteLong(9001);
        writer.WriteLong(9002);
        writer.WriteByte((byte) count);
        for (int i = 0; i < count; i++) {
            writer.WriteByte((byte) (i % 2));
            writer.WriteShort((short) (20 + i));
        }
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(9001));
        Assert.That(reader.ReadLong(), Is.EqualTo(9002));
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(reader)!;
        Assert.That(locks, Has.Count.EqualTo(count));
        for (int i = 0; i < count; i++) {
            Assert.That(locks[i], Is.EqualTo(new ItemRemakeRules.Lock(i % 2 == 1, (short) (20 + i))));
        }
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Mode 1 of 0x0076: i64 scroll, i64 item, i64 ingredient, u8 kept-lock flag, then the kept options.
    [Test]
    public void ScrollBytes() {
        var writer = new ByteWriter();
        writer.WriteLong(1);
        writer.WriteLong(2);
        writer.WriteLong(3);
        writer.WriteBool(true);
        writer.WriteByte(2);
        writer.WriteByte(1);
        writer.WriteShort(7);
        writer.WriteByte(0);
        writer.WriteShort(8);
        var reader = new ByteReader(writer.ToArray(), 0);
        reader.ReadLong();
        reader.ReadLong();
        reader.ReadLong();
        Assert.That(reader.ReadBool(), Is.True);
        List<ItemRemakeRules.Lock> locks = ItemRemakeRules.ReadLocks(reader)!;
        Assert.That(locks, Is.EqualTo(new[] { new ItemRemakeRules.Lock(true, 7), new ItemRemakeRules.Lock(false, 8) }));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Allowed() {
        Assert.That(ItemRemakeRules.LocksAllowed(0), Is.True);
        Assert.That(ItemRemakeRules.LocksAllowed(1), Is.True);
        Assert.That(ItemRemakeRules.LocksAllowed(2), Is.False);
        Assert.That(ItemRemakeRules.LocksAllowed(2, 2), Is.True);
        Assert.That(ItemRemakeRules.LocksAllowed(-1, 2), Is.False);
    }

    // GetLockItemId checks armor, weapon, accessory, then pet; anything else has no lock item.
    [Test]
    public void LockTagOrder() {
        Assert.That(ItemRemakeRules.LockTag(true, true, true, true), Is.EqualTo(ItemTag.LockItemOptionArmor));
        Assert.That(ItemRemakeRules.LockTag(false, true, true, true), Is.EqualTo(ItemTag.LockItemOptionWeapon));
        Assert.That(ItemRemakeRules.LockTag(false, false, true, true), Is.EqualTo(ItemTag.LockItemOptionAccessory));
        Assert.That(ItemRemakeRules.LockTag(false, false, false, true), Is.EqualTo(ItemTag.LockItemOptionPet));
        Assert.That(ItemRemakeRules.LockTag(false, false, false, false), Is.Null);
    }

    // An equip takes a lock item of its own rarity; a pet takes one of any rarity.
    [Test]
    public void LockItemRarity() {
        int equip = ItemRemakeRules.LockRarity(false, 4);
        Assert.That(ItemRemakeRules.IsLockItem(ItemTag.LockItemOptionAccessory, equip, ItemTag.LockItemOptionAccessory, 4), Is.True);
        Assert.That(ItemRemakeRules.IsLockItem(ItemTag.LockItemOptionAccessory, equip, ItemTag.LockItemOptionAccessory, 5), Is.False);
        Assert.That(ItemRemakeRules.IsLockItem(ItemTag.LockItemOptionAccessory, equip, ItemTag.LockItemOptionArmor, 4), Is.False);

        int pet = ItemRemakeRules.LockRarity(true, 6);
        Assert.That(pet, Is.EqualTo(0));
        Assert.That(ItemRemakeRules.IsLockItem(ItemTag.LockItemOptionPet, pet, ItemTag.LockItemOptionPet, 3), Is.True);
        Assert.That(ItemRemakeRules.IsLockItem(ItemTag.LockItemOptionPet, pet, ItemTag.LockItemOptionPet, 5), Is.True);
    }

    // Only a lock item the character can use counts (PC::CanUseItem): not expired, premium club only with it, not
    // bound to another character.
    [Test]
    public void LockItemUsable() {
        Assert.That(ItemRemakeRules.LockItemUsable(false, false, false, 0, 7), Is.True);
        Assert.That(ItemRemakeRules.LockItemUsable(false, false, false, 7, 7), Is.True);
        Assert.That(ItemRemakeRules.LockItemUsable(true, false, false, 0, 7), Is.False);
        Assert.That(ItemRemakeRules.LockItemUsable(false, true, false, 0, 7), Is.False);
        Assert.That(ItemRemakeRules.LockItemUsable(false, true, true, 0, 7), Is.True);
        Assert.That(ItemRemakeRules.LockItemUsable(false, false, false, 8, 7), Is.False);
    }

    [Test]
    public void RemakeCondition_KeepsGearAndPetsApart() {
        Assert.That(ItemRemakeRules.RemakeCondition(false), Is.EqualTo(ConditionType.item_remake_option));
        Assert.That(ItemRemakeRules.RemakeCondition(true), Is.EqualTo(ConditionType.pet_remake_option));
    }

    // The record of one item's changes, gear and pets apart, kept at the highest count.
    [Test]
    public void RecordCondition_KeepsGearAndPetsApart() {
        Assert.That(ItemRemakeRules.RecordCondition(false), Is.EqualTo(ConditionType.item_remake_option_record));
        Assert.That(ItemRemakeRules.RecordCondition(true), Is.EqualTo(ConditionType.pet_remake_option_record));
        Assert.That(AchievementManager.KeepsHighest(ConditionType.item_remake_option_record), Is.True);
        Assert.That(AchievementManager.KeepsHighest(ConditionType.pet_remake_option_record), Is.True);
        Assert.That(AchievementManager.KeepsHighest(ConditionType.item_remake_option), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.item_remake_option_record, 5, 5), Is.False);
        Assert.That(AchievementManager.Advances(ConditionType.item_remake_option_record, 5, 6), Is.True);
    }

    // Owned fragments first, 1000 meso for each one lacking (KMS2 charges whole thousands).
    [TestCase(396, 500, 396, 0)]
    [TestCase(396, 396, 396, 0)]
    [TestCase(396, 69, 69, 327000)]
    [TestCase(300, 0, 0, 300000)]
    public void FragmentFee(int need, long owned, int fragments, long meso) {
        Assert.That(ItemRemakeRules.FragmentFee(need, owned), Is.EqualTo((fragments, meso)));
    }
}
