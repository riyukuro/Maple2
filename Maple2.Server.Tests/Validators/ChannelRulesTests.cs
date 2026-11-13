using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Validators;

public class ChannelRulesTests {
    // Channel 0x003B: i16 channel. The player here is in channel 2.
    [TestCase((short) 1, false, false, true, true)]
    [TestCase((short) 3, false, false, true, true)]
    [TestCase((short) 2, false, false, true, false)] // the own channel
    [TestCase((short) 1, true, false, true, false)] // dead
    [TestCase((short) 1, false, true, true, false)] // in combat
    [TestCase((short) 1, false, false, false, false)] // a dungeon or random room
    [TestCase((short) 0, false, false, true, false)]
    [TestCase((short) -1, false, false, true, false)]
    public void ChannelPacket(short wire, bool dead, bool inBattle, bool normalField, bool expected) {
        var writer = new ByteWriter();
        writer.WriteShort(wire);
        var packet = new ByteReader(writer.ToArray(), 0);
        short channel = packet.ReadShort();
        Assert.That(packet.Available, Is.EqualTo(0));
        Assert.That(ChannelRules.CanChange(2, channel, dead, inBattle, normalField), Is.EqualTo(expected));
    }
}
