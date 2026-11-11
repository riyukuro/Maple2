using Maple2.Model.Enum;
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
}
