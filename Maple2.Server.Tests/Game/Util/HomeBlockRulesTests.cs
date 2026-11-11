using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HomeBlockRulesTests {
    [Test]
    public void CheckAdd_ReturnsTheClientsErrorCodes() {
        Assert.That(HomeBlockRules.CheckAdd(1, null, false, 0, 100), Is.EqualTo(HomeBlockPacket.Error.NoUser));
        Assert.That(HomeBlockRules.CheckAdd(1, 1, false, 0, 100), Is.EqualTo(HomeBlockPacket.Error.SelfBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, true, 0, 100), Is.EqualTo(HomeBlockPacket.Error.AlreadyBlocked));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, 100, 100), Is.EqualTo(HomeBlockPacket.Error.MaxBlock));
        Assert.That(HomeBlockRules.CheckAdd(1, 2, false, 99, 100), Is.Null);
    }

    [Test]
    public void MaxBlocks_FallsBackWhenTheConstantIsMissing() {
        Assert.That(HomeBlockRules.MaxBlocks(30), Is.EqualTo(30));
        Assert.That(HomeBlockRules.MaxBlocks(0), Is.EqualTo(HomeBlockRules.DefaultMaxBlocks));
    }

    // Modes 2 and 5: i64, string memo. A banned memo is refused, an empty one is not looked up.
    [Test]
    public void MemoBanned_UsesTheBanWordCheck() {
        var writer = new Maple2.PacketLib.Tools.ByteWriter();
        writer.WriteLong(42);
        writer.WriteUnicodeString("bad memo");
        var reader = new Maple2.PacketLib.Tools.ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadLong(), Is.EqualTo(42));
        string memo = HomeBlockRules.TrimMemo(reader.ReadUnicodeString());
        Assert.That(reader.Available, Is.EqualTo(0));
        Assert.That(HomeBlockRules.MemoBanned(memo, text => text.Contains("bad")), Is.True);
        Assert.That(HomeBlockRules.MemoBanned("fine", text => text.Contains("bad")), Is.False);
        Assert.That(HomeBlockRules.MemoBanned("", _ => true), Is.False);
    }

    [Test]
    public void TrimMemo_CutsAtTheInputLength() {
        Assert.That(HomeBlockRules.TrimMemo("note"), Is.EqualTo("note"));
        Assert.That(HomeBlockRules.TrimMemo(new string('a', 300)).Length, Is.EqualTo(50));
    }
}
