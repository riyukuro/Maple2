using System;
using System.IO;
using System.Linq;
using System.Xml;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// Enchant scrolls: the server pack's table/Server/enchantScroll.xml weights (successProp) and disableBreak.
// Made-up ids; the weights of the first row are the shape of the random scrolls in the pack.
public class EnchantScrollRateTests {
    private const string Xml = """
        <ms2>
          <scroll id="1" scrollType="3" grade="6,7,8" successProp="7000,2000,1000" minLv="48" maxLv="50" slot="30" rank="1" isLockTrade="False" />
          <scroll id="1" scrollType="3" grade="6,7,8" successProp="7000,2000,1000" minLv="48" maxLv="50" slot="30,13" rank="1" isLockTrade="False" feature="Other" />
          <scroll id="2" scrollType="1" grade="10" successProp="10000" minLv="20" maxLv="50" slot="32" rank="1,2,3" isLockTrade="False" disableBreak="1" />
          <scroll id="3" scrollType="2" grade="0" successProp="10000" minLv="20" maxLv="50" slot="32" rank="1" isLockTrade="False" />
          <scroll id="4" scrollType="3" grade="8,9" successProp="10000" minLv="20" maxLv="50" slot="32" rank="1" isLockTrade="False" />
        </ms2>
        """;

    private static System.Collections.Generic.Dictionary<int, ServerEnchantScroll.Entry> Rows() {
        return ServerEnchantScroll.Read(XmlReader.Create(new StringReader(Xml)));
    }

    private static EnchantScrollMetadata Scroll(EnchantScrollType type, int[] enchants, int[]? rates, bool disableBreak = false) {
        return new EnchantScrollMetadata(type, 20, 50, enchants, [32], [1], rates, disableBreak);
    }

    // Returns the given NextInt64 value, to land the roll at a known point.
    private sealed class FixedRandom(long value) : Random {
        public override long NextInt64(long maxValue) => value;
        public override int Next(int maxValue) => (int) value;
    }

    [Test]
    public void Read_RatesAndDisableBreakById() {
        var rows = Rows();
        Assert.That(rows.Keys, Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        Assert.That(rows[1].Rates, Is.EqualTo(new[] { 7000, 2000, 1000 }));
        Assert.That(rows[1].DisableBreak, Is.False);
        Assert.That(rows[2].Rates, Is.EqualTo(new[] { 10000 }));
        Assert.That(rows[2].DisableBreak, Is.True);
    }

    [Test]
    public void RatesFor_FollowsSortedGrades() {
        var row = new ServerEnchantScroll.Entry([8, 6, 7], [1000, 7000, 2000], false);
        Assert.That(ServerEnchantScroll.RatesFor(row, [6, 7, 8]), Is.EqualTo(new[] { 7000, 2000, 1000 }));
        // One weight for two grades (id 4), a grade the row lacks, or no row: no weights.
        Assert.That(ServerEnchantScroll.RatesFor(Rows()[4], [8, 9]), Is.Null);
        Assert.That(ServerEnchantScroll.RatesFor(row, [6, 7, 9]), Is.Null);
        Assert.That(ServerEnchantScroll.RatesFor(null, [6]), Is.Null);
    }

    [TestCase(0, 6)]
    [TestCase(6999, 6)]
    [TestCase(7000, 7)]
    [TestCase(8999, 7)]
    [TestCase(9000, 8)]
    [TestCase(9999, 8)]
    public void Roll_IsWeighted(long roll, int expected) {
        EnchantScrollMetadata scroll = Scroll(EnchantScrollType.Random, [6, 7, 8], [7000, 2000, 1000]);
        Assert.That(EnchantScrollHandler.RollEnchant(scroll, new FixedRandom(roll)), Is.EqualTo(expected));
    }

    [Test]
    public void Roll_WeightsNeedNotSumTo10000() {
        // The pack has a random scroll whose weights add up to 10200; the weights are relative.
        EnchantScrollMetadata scroll = Scroll(EnchantScrollType.Random, [6, 7], [5100, 5100]);
        Assert.That(EnchantScrollHandler.RollEnchant(scroll, new FixedRandom(10199)), Is.EqualTo(7));
    }

    [Test]
    public void Roll_SeededShare() {
        EnchantScrollMetadata scroll = Scroll(EnchantScrollType.Random, [6, 7, 8], [7000, 2000, 1000]);
        var random = new Random(84);
        int[] rolls = Enumerable.Range(0, 20000).Select(_ => EnchantScrollHandler.RollEnchant(scroll, random)).ToArray();
        Assert.That(rolls.Count(level => level == 6) / 20000.0, Is.EqualTo(0.7).Within(0.02));
        Assert.That(rolls.Count(level => level == 8) / 20000.0, Is.EqualTo(0.1).Within(0.02));
    }

    [Test]
    public void Roll_WithoutWeightsIsEven() {
        Assert.That(EnchantScrollHandler.RollEnchant(Scroll(EnchantScrollType.Random, [6, 7, 8], null), new FixedRandom(2)), Is.EqualTo(8));
        Assert.That(EnchantScrollHandler.RollEnchant(Scroll(EnchantScrollType.Random, [6, 7, 8], [1, 2]), new FixedRandom(1)), Is.EqualTo(7));
        Assert.That(EnchantScrollHandler.RollEnchant(Scroll(EnchantScrollType.Random, [6, 7], [0, 0]), new FixedRandom(0)), Is.EqualTo(6));
    }

    // UIEnchantScrollDialog FUN_140f3b920 reads: long scroll uid, short type, byte disableBreak, ...
    [TestCase(false, (byte) 0)]
    [TestCase(true, (byte) 1)]
    public void UseScroll_SendsDisableBreak(bool disableBreak, byte expected) {
        var limit = new ItemMetadataLimit(Gender.All, 20, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var property = new ItemMetadataProperty(false, 0, 1, 0, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var metadata = new ItemMetadata(20000001, "20000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var item = new Item(metadata, 1) { Uid = 0x0102030405060708 };

        ByteWriter writer = EnchantScrollPacket.UseScroll(item, Scroll(EnchantScrollType.Enchant, [10], [10000], disableBreak));
        byte[] body = writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();
        Assert.That(body[0], Is.EqualTo((byte) 0)); // UseScroll
        Assert.That(BitConverter.ToInt64(body, 1), Is.EqualTo(0x0102030405060708));
        Assert.That(BitConverter.ToInt16(body, 9), Is.EqualTo((short) EnchantScrollType.Enchant));
        Assert.That(body[11], Is.EqualTo(expected));
        Assert.That(BitConverter.ToInt32(body, 12), Is.EqualTo(10)); // target level
    }
}
