using System;
using Maple2.Model.Validators;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Game.Packets;

public class UgcCostTableTests {
    // Ugc mode 21 as KMS2 sends it at login: i32 count, then i64 step and i32 value per row.
    [Test]
    public void LoginTableBytes() {
        Assert.That(Convert.ToHexString(UgcPacket.CostTable(UgcMarketRules.CostRows).ToArray()), Is.EqualTo(
            "6B00" + "15" + "05000000"
            + "0000000000000000" + "46000000"
            + "8813000000000000" + "3C000000"
            + "1027000000000000" + "32000000"
            + "A086010000000000" + "28000000"
            + "400D030000000000" + "14000000"));
    }

    [Test]
    public void EmptyTableBytes() {
        Assert.That(Convert.ToHexString(UgcPacket.CostTable([]).ToArray()), Is.EqualTo("6B00" + "15" + "00000000"));
    }
}
