using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class GemstoneUnequipRulesTests {
    [Test]
    public void PacketIsUidAndSlot() {
        var writer = new ByteWriter();
        writer.WriteLong(0x1122334455667788);
        writer.WriteByte(2);
        var packet = new ByteReader(writer.ToArray(), 0);
        Assert.That(packet.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(false, true, GemstoneUnequipRules.Result.NoGemstone)]
    [TestCase(true, false, GemstoneUnequipRules.Result.NoRoom)]
    [TestCase(false, false, GemstoneUnequipRules.Result.NoGemstone)]
    public void NothingIsPaidWhenTheGemstoneCannotComeOut(bool made, bool room, GemstoneUnequipRules.Result expected) {
        int paid = 0;
        System.Func<bool> pay = () => {
            paid++;
            return true;
        };
        Assert.That(GemstoneUnequipRules.TakeOut(made, room, pay), Is.EqualTo(expected));
        Assert.That(paid, Is.EqualTo(0));
    }

    [Test]
    public void PaysOnceWhenItCan() {
        int paid = 0;
        Assert.That(GemstoneUnequipRules.TakeOut(true, true, () => {
            paid++;
            return true;
        }), Is.EqualTo(GemstoneUnequipRules.Result.Ok));
        Assert.That(paid, Is.EqualTo(1));
        Assert.That(GemstoneUnequipRules.TakeOut(true, true, () => false), Is.EqualTo(GemstoneUnequipRules.Result.NotPaid));
    }
}
