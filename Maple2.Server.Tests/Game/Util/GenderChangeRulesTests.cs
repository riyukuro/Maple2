using Maple2.Model.Enum;
using Maple2.Model.Game;
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

    // KMS2 Notice after the change: 05 4000 01 01000000 f3020000 00000000.
    [Test]
    public void DoneNoticeBytes() {
        byte[] packet = NoticePacket.Disconnect(StringCode.s_change_gender_err_equip_items).ToArray();
        Assert.That(System.Convert.ToHexString(packet[2..]), Is.EqualTo("05400001010000 00F3020000 00000000".Replace(" ", "")));
    }
}
