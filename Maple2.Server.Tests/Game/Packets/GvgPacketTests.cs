using System;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// The layout is the client's reader, CNetworkService::OnGvg kind 7 and CContentShutdown::Decode.
public class GvgPacketTests {
    [Test]
    public void ContentShutdown_NoneShut_OneGroupCountZero() {
        Assert.That(Convert.ToHexString(GvgPacket.ContentShutdown([]).ToArray()), Is.EqualTo("6D00" + "01000000" + "07000000" + "0000"));
    }

    [Test]
    public void ContentShutdown_ListsEachContent() {
        Assert.That(Convert.ToHexString(GvgPacket.ContentShutdown([0x15, 0x19]).ToArray()),
            Is.EqualTo("6D00" + "01000000" + "07000000" + "0200" + "1500" + "1900"));
    }

    [Test]
    public void Parse_UnsetIsAllOn() {
        Assert.That(ContentShutdownConfig.Parse(null), Is.Empty);
        Assert.That(ContentShutdownConfig.Parse("  "), Is.Empty);
    }

    [Test]
    public void Parse_DecimalAndHex_SortedDistinct_OutOfRangeDropped() {
        Assert.That(ContentShutdownConfig.Parse("0x19, 21,25 ,0,117,-3,x,0x15"), Is.EqualTo(new short[] { 21, 25 }));
        Assert.That(ContentShutdownConfig.Parse("1,116"), Is.EqualTo(new short[] { 1, 116 }));
    }
}
