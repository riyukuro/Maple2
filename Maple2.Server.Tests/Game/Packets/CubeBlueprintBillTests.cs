using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

public class CubeBlueprintBillTests {
    // Client: FUN_140ee83f0 (uiugccubebilldialog.cpp:283) reads byte count, int, count x (byte, long), bool preview.
    [TestCase(true, 1)]
    [TestCase(false, 0)]
    public void BillEndsWithThePreviewFlag(bool preview, byte expected) {
        var costs = new Dictionary<FurnishingCurrencyType, long> { { FurnishingCurrencyType.Meso, 0x1122334455667788 } };
        ByteWriter writer = CubePacket.CalculateEstimate(costs, 7, preview);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(0x43));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) FurnishingCurrencyType.Meso));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
        Assert.That(reader.ReadByte(), Is.EqualTo(expected));
        Assert.That(writer.Length, Is.EqualTo(18));
    }

    [Test]
    public void EmptyBillIsCountZeroThenFlag() {
        ByteWriter writer = CubePacket.CalculateEstimate(new Dictionary<FurnishingCurrencyType, long>(), 0, preview: false);
        Assert.That(writer.Length, Is.EqualTo(9));
        Assert.That(writer.Buffer[2], Is.EqualTo(0x43));
        Assert.That(writer.Buffer[8], Is.EqualTo(0));
    }

    [Test]
    public void OnlyTheStagedItemConfirms() {
        Assert.That(BlueprintBillRule.Confirms(true, 55, 55), Is.True);
        Assert.That(BlueprintBillRule.Confirms(true, 55, 56), Is.False);
        Assert.That(BlueprintBillRule.Confirms(false, 55, 55), Is.False);
        Assert.That(BlueprintBillRule.Confirms(true, 0, 0), Is.False);
    }
}
