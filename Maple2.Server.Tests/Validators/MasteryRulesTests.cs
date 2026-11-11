using Maple2.Model.Error;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class MasteryRulesTests {
    // Mastery 0x0092 mode 2: u8 mode, i32 recipe id.
    [Test]
    public void CraftPacket() {
        var packet = new ByteReader(new byte[] { 0x02, 0x40, 0x42, 0x0F, 0x00 }, 0);
        Assert.That(packet.ReadByte(), Is.EqualTo(2));
        Assert.That(packet.ReadInt(), Is.EqualTo(1000000));
        Assert.That(packet.Available, Is.EqualTo(0));
    }

    [TestCase(10, 20, 0, false, 100L, 0L, MasteryError.s_mastery_error_lack_mastery)]
    [TestCase(20, 20, 0, false, 100L, 0L, (MasteryError) 0)]
    [TestCase(0, 0, 2, false, 100L, 0L, MasteryError.s_mastery_error_lack_quest)]
    [TestCase(0, 0, 2, true, 100L, 0L, (MasteryError) 0)] // one of the two quests is enough
    [TestCase(0, 0, 0, false, 99L, 100L, MasteryError.s_mastery_error_lack_meso)]
    [TestCase(0, 0, 0, false, 100L, 100L, (MasteryError) 0)]
    [TestCase(10, 20, 2, false, 0L, 100L, MasteryError.s_mastery_error_lack_mastery)] // mastery is checked first
    [TestCase(20, 20, 2, false, 0L, 100L, MasteryError.s_mastery_error_lack_quest)]
    public void CraftChecks(int mastery, int requiredMastery, int questCount, bool anyQuest, long meso, long requiredMeso, MasteryError expected) {
        Assert.That(MasteryRules.CraftError(mastery, requiredMastery, questCount, anyQuest, meso, requiredMeso), Is.EqualTo(expected));
    }
}
