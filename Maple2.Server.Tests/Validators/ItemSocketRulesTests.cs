using Maple2.Model.Enum;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class ItemSocketRulesTests {
    // An outfit with sockets (Northern Fur Earrings, skin type 2) is listed by the socket unlock page from the outfit
    // tab, so the server looks there too.
    [Test]
    public void Inventories_AreTheGearAndOutfitTabs() {
        Assert.That(ItemSocketRules.Inventories, Is.EqualTo(new[] { InventoryType.Gear, InventoryType.Outfit }));
    }

    // A gem in an equipment socket (skin type 0) counts for equip_gemstone_puton (70600031), one in an outfit's socket
    // for skin_gemstone_puton; taking it out counts the putoff of the same kind.
    [TestCase(0, ConditionType.equip_gemstone_puton, ConditionType.equip_gemstone_putoff)]
    [TestCase(1, ConditionType.skin_gemstone_puton, ConditionType.skin_gemstone_putoff)]
    [TestCase(99, ConditionType.skin_gemstone_puton, ConditionType.skin_gemstone_putoff)]
    public void Gemstone_CountsTheSocketKindOfTheItem(int skinType, ConditionType putOn, ConditionType putOff) {
        Assert.That(ItemSocketRules.GemstonePutOnCondition(skinType), Is.EqualTo(putOn));
        Assert.That(ItemSocketRules.GemstonePutOffCondition(skinType), Is.EqualTo(putOff));
    }
}
