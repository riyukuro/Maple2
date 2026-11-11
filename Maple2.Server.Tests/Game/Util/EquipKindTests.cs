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
