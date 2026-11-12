using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class EquipKindTests {
    [Test]
    public void Outfit2NeedsAnActiveTab() {
        Assert.That(EquipKind.CanUseOutfit2(EquipSlot.CL, tabActive: true), Is.True);
        Assert.That(EquipKind.CanUseOutfit2(EquipSlot.CL, tabActive: false), Is.False);
    }

    [Test]
    public void Outfit2RefusesHairAndFace() {
        foreach (EquipSlot slot in new[] { EquipSlot.HR, EquipSlot.ER, EquipSlot.FA, EquipSlot.FD }) {
            Assert.That(EquipKind.CanUseOutfit2(slot, tabActive: true), Is.False, slot.ToString());
        }
    }

    [Test]
    public void Resolve_GearItemGoesToGearForEveryKind() {
        foreach (byte kind in new byte[] { 0, 1, 2 }) {
            Assert.That(EquipKind.Resolve(kind, isSkin: false, EquipSlot.MT, tabActive: true), Is.EqualTo(EquipKind.Gear), $"kind {kind}");
            Assert.That(EquipKind.Resolve(kind, isSkin: false, EquipSlot.MT, tabActive: false), Is.EqualTo(EquipKind.Gear), $"kind {kind}");
        }
    }

    [Test]
    public void Resolve_OutfitItemByKind() {
        // Kind 0 (the server's own equip request, auto-equip) and 1 (skin-tab key held) go to the outfit tab.
        Assert.That(EquipKind.Resolve(0, isSkin: true, EquipSlot.MT, tabActive: true), Is.EqualTo(EquipKind.Outfit));
        Assert.That(EquipKind.Resolve(1, isSkin: true, EquipSlot.MT, tabActive: true), Is.EqualTo(EquipKind.Outfit));
        // Kind 2 (a plain right click) goes to the second tab while it runs.
        Assert.That(EquipKind.Resolve(2, isSkin: true, EquipSlot.MT, tabActive: true), Is.EqualTo(EquipKind.Outfit2));
    }

    [Test]
    public void Resolve_Kind2FallsBackToOutfitTab() {
        // The second tab never bought or run out: the right click still puts the item on.
        Assert.That(EquipKind.Resolve(2, isSkin: true, EquipSlot.MT, tabActive: false), Is.EqualTo(EquipKind.Outfit));
        // Hair and face are replaced in the first tab only.
        Assert.That(EquipKind.Resolve(2, isSkin: true, EquipSlot.HR, tabActive: true), Is.EqualTo(EquipKind.Outfit));
    }

    [Test]
    public void Resolve_UnknownKindRefused() {
        Assert.That(EquipKind.Resolve(3, isSkin: true, EquipSlot.MT, tabActive: true), Is.Null);
        Assert.That(EquipKind.Resolve(3, isSkin: false, EquipSlot.MT, tabActive: true), Is.Null);
    }

    [Test]
    public void CanSwapHands_OnlyTwoEitherHandWeapons() {
        var dagger = new ItemType(13100000);
        var star = new ItemType(13400000);
        var longsword = new ItemType(13200000);

        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.LH, dagger, dagger), Is.True);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.LH, EquipSlot.RH, star, star), Is.True);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.LH, dagger, null), Is.False);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.LH, longsword, dagger), Is.False);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.LH, dagger, longsword), Is.False);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.CP, dagger, dagger), Is.False);
        Assert.That(EquipKind.CanSwapHands(EquipSlot.RH, EquipSlot.RH, dagger, dagger), Is.False);
    }
}
