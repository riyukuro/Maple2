using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class GenderChangeRulesTests {
    [Test]
    public void OtherGender() {
        Assert.That(GenderChangeRules.Other(Gender.Male), Is.EqualTo(Gender.Female));
        Assert.That(GenderChangeRules.Other(Gender.Female), Is.EqualTo(Gender.Male));
    }

    [Test]
    public void LookSlotsAreTheFourKms2TakesOff() {
        Assert.That(GenderChangeRules.LookSlots, Is.EqualTo(new[] { EquipSlot.HR, EquipSlot.FA, EquipSlot.FD, EquipSlot.ER }));
    }

    [Test]
    public void OnlyGenderLimitedItemsOutsideTheLookBlock() {
        Assert.That(GenderChangeRules.BlocksChange(EquipSlot.CL, Gender.Female), Is.True);
        Assert.That(GenderChangeRules.BlocksChange(EquipSlot.CL, Gender.All), Is.False);
        Assert.That(GenderChangeRules.BlocksChange(EquipSlot.HR, Gender.Female), Is.False);
        Assert.That(GenderChangeRules.BlocksChange(EquipSlot.ER, Gender.Male), Is.False);
    }

    [Test]
    public void PickDefaultPrefersTheGendersOwnItem() {
        (int, Gender)[] hair = [(10200001, Gender.Male), (10200004, Gender.All), (10200006, Gender.Female), (10200007, Gender.Female)];
        Assert.That(GenderChangeRules.PickDefault(hair, Gender.Female), Is.EqualTo(10200006));
        Assert.That(GenderChangeRules.PickDefault(hair, Gender.Male), Is.EqualTo(10200001));
        Assert.That(GenderChangeRules.PickDefault([(10500001, Gender.All)], Gender.Male), Is.EqualTo(10500001));
        Assert.That(GenderChangeRules.PickDefault([(10200006, Gender.Female)], Gender.Male), Is.EqualTo(0));
    }

    // The done notice: Notice 5, flags 0x40, one string code, client string 755.
    [Test]
    public void DoneNoticeBytes() {
        byte[] packet = NoticePacket.Disconnect(StringCode.s_change_gender_err_equip_items).ToArray();
        Assert.That(System.Convert.ToHexString(packet[2..]), Is.EqualTo("05400001010000 00F3020000 00000000".Replace(" ", "")));
    }

    [Test]
    public void WeddingStates() {
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.Single), Is.True);
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.Divorced), Is.True);
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.Engaged), Is.False);
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.Married), Is.False);
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.DivorceFiledByGroom), Is.False);
        Assert.That(GenderChangeRules.WeddingAllows(MaritalStatus.DivorceFiledByBride), Is.False);
    }

    private static ItemMetadata Metadata(int id, EquipSlot slot) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [slot], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private static Item Worn(int id, EquipSlot slot, long uid) {
        return new Item(Metadata(id, slot)) { Uid = uid, Group = ItemGroup.Outfit, Slot = (short) slot };
    }

    // What the storage does: the same item with its row's uid.
    private static Item Stored(Item look, long uid) => new(look.Metadata) { Uid = uid, Appearance = look.Appearance };

    private static readonly (EquipSlot Slot, ItemMetadata Metadata)[] Picks = [
        (EquipSlot.HR, Metadata(10200006, EquipSlot.HR)), (EquipSlot.FA, Metadata(10300002, EquipSlot.FA)),
        (EquipSlot.FD, Metadata(10400000, EquipSlot.FD)), (EquipSlot.ER, Metadata(10500001, EquipSlot.ER)),
    ];

    private static Character Male() => new() { Name = "a", Gender = Gender.Male, Mastery = new Mastery() };

    private static Dictionary<EquipSlot, Item> FullLook() => new() {
        [EquipSlot.HR] = Worn(10200001, EquipSlot.HR, 1), [EquipSlot.FA] = Worn(10300001, EquipSlot.FA, 2),
        [EquipSlot.FD] = Worn(10400001, EquipSlot.FD, 3), [EquipSlot.ER] = Worn(10500002, EquipSlot.ER, 4),
        [EquipSlot.CL] = Worn(11400001, EquipSlot.CL, 5),
    };

    // The whole use: the voucher is consumed, the four look slots (the ear too) hold the new stored items,
    // the old ones were taken off, other outfit slots are untouched and the gender flips.
    [Test]
    public void ApplySwapsTheFourSlotsAndFlipsTheGender() {
        Character character = Male();
        Dictionary<EquipSlot, Item> outfit = FullLook();
        outfit[EquipSlot.HR].Appearance!.Color = new EquipColor(default, default, default, 7);
        int vouchers = 1;
        long nextUid = 100;
        var takenOff = new List<long>();
        var unstored = new List<long>();

        bool changed = GenderChangeRules.Apply(character, outfit, Picks,
            store: look => Stored(look, nextUid++),
            unstore: look => unstored.Add(look.Uid),
            consume: () => vouchers-- > 0,
            takeOff: old => takenOff.Add(old.Uid));

        Assert.That(changed, Is.True);
        Assert.That(vouchers, Is.EqualTo(0));
        Assert.That(character.Gender, Is.EqualTo(Gender.Female));
        Assert.That(takenOff, Is.EquivalentTo(new long[] { 1, 2, 3, 4 }));
        Assert.That(unstored, Is.Empty);
        Assert.That(outfit[EquipSlot.HR].Id, Is.EqualTo(10200006));
        Assert.That(outfit[EquipSlot.FA].Id, Is.EqualTo(10300002));
        Assert.That(outfit[EquipSlot.FD].Id, Is.EqualTo(10400000));
        Assert.That(outfit[EquipSlot.ER].Id, Is.EqualTo(10500001));
        Assert.That(outfit[EquipSlot.CL].Uid, Is.EqualTo(5));
        foreach (EquipSlot slot in GenderChangeRules.LookSlots) {
            Assert.That(outfit[slot].Uid, Is.GreaterThanOrEqualTo(100));
            Assert.That(outfit[slot].Group, Is.EqualTo(ItemGroup.Outfit));
            Assert.That(outfit[slot].Slot, Is.EqualTo((short) slot));
        }
        Assert.That(outfit[EquipSlot.HR].Appearance!.Color.PaletteId, Is.EqualTo(7));
    }

    // No face decoration and no ear worn: none is added, and no row is stored for them.
    [Test]
    public void ApplyAddsNothingToAnEmptyDecorationOrEarSlot() {
        Character character = Male();
        Dictionary<EquipSlot, Item> outfit = FullLook();
        outfit.Remove(EquipSlot.FD);
        outfit.Remove(EquipSlot.ER);
        int storedCount = 0;

        bool changed = GenderChangeRules.Apply(character, outfit, Picks,
            store: look => Stored(look, 100 + storedCount++), unstore: _ => { }, consume: () => true, takeOff: _ => { });

        Assert.That(changed, Is.True);
        Assert.That(storedCount, Is.EqualTo(2));
        Assert.That(outfit.ContainsKey(EquipSlot.FD), Is.False);
        Assert.That(outfit.ContainsKey(EquipSlot.ER), Is.False);
    }

    // A failed step changes nothing: the voucher, the gender and the worn items stay, and the rows
    // stored so far are taken back.
    [TestCase(true, false, 4)]  // the voucher cannot be consumed
    [TestCase(false, true, 2)]  // the third item cannot be stored
    public void ApplyFailureKeepsEverything(bool storeAll, bool consume, int takenBack) {
        Character character = Male();
        Dictionary<EquipSlot, Item> outfit = FullLook();
        int storedCount = 0;
        bool consumed = false;
        var unstored = new List<long>();
        var takenOff = new List<long>();

        bool changed = GenderChangeRules.Apply(character, outfit, Picks,
            store: look => {
                if (!storeAll && storedCount == 2) {
                    return null;
                }
                return Stored(look, 100 + storedCount++);
            },
            unstore: look => unstored.Add(look.Uid),
            consume: () => { consumed = consume; return consume; },
            takeOff: old => takenOff.Add(old.Uid));

        Assert.That(changed, Is.False);
        Assert.That(consumed, Is.False);
        Assert.That(character.Gender, Is.EqualTo(Gender.Male));
        Assert.That(takenOff, Is.Empty);
        Assert.That(unstored, Has.Count.EqualTo(takenBack));
        Assert.That(outfit.Values.Select(item => item.Uid), Is.EquivalentTo(new long[] { 1, 2, 3, 4, 5 }));
    }

    // Without a hair or face pick the change is refused before the voucher is touched.
    [Test]
    public void ApplyNeedsHairAndFace() {
        Character character = Male();
        bool consumed = false;
        bool changed = GenderChangeRules.Apply(character, FullLook(), Picks[1..],
            store: look => Stored(look, 100), unstore: _ => { }, consume: () => consumed = true, takeOff: _ => { });
        Assert.That(changed, Is.False);
        Assert.That(consumed, Is.False);
        Assert.That(character.Gender, Is.EqualTo(Gender.Male));
    }
}
