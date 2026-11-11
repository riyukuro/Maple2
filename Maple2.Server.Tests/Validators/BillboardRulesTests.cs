using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class BillboardRulesTests {
    // RequestItemUse 0x0019 with a billboard item: after the item uid, one string title'description'flag.
    [TestCase("Shop'Come in'1", true, true, BillboardInstallResult.Success)]
    [TestCase("Shop'Come in", true, true, BillboardInstallResult.Fail)]
    [TestCase("Shop'Come in'1", false, true, BillboardInstallResult.Fail)]
    [TestCase("Shop'Come in'1", true, false, BillboardInstallResult.Fail)]
    public void InstallResult(string text, bool itemParameters, bool placed, BillboardInstallResult expected) {
        var writer = new ByteWriter();
        writer.WriteUnicodeString(text);
        var packet = new ByteReader(writer.ToArray(), 0);
        int parts = packet.ReadUnicodeString().Split("'").Length;
        Assert.That(BillboardRules.InstallResult(parts, itemParameters, placed), Is.EqualTo(expected));
    }

    // The answer the billboard dialog reads: u8 mode 7, i32 result.
    [TestCase(BillboardInstallResult.Success, (byte) 0)]
    [TestCase(BillboardInstallResult.Fail, (byte) 1)]
    [TestCase(BillboardInstallResult.MaxPerField, (byte) 3)]
    public void InstallAnswerBytes(BillboardInstallResult result, byte low) {
        byte[] bytes = PlayerHostPacket.BillboardInstallResult(result).ToArray();
        Assert.That(bytes.Length, Is.EqualTo(2 + 1 + 4));
        Assert.That(bytes[2], Is.EqualTo(7));
        Assert.That(bytes[3], Is.EqualTo(low));
        Assert.That(bytes[4] | bytes[5] | bytes[6], Is.EqualTo(0));
    }
}
