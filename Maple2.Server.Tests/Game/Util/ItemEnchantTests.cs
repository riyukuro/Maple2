using Maple2.Model.Game;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.Util;

// The enchant block of an item as CItemEnchantInfo reads it (commonfiles/itemenchantinfo.cpp:28).
public class ItemEnchantTests {
    private static byte[] Bytes(byte flag) {
        var expected = new ByteWriter();
        expected.WriteInt(3);
        expected.WriteInt(5);
        expected.WriteByte(2);
        expected.WriteBytes(new byte[16]);
        expected.WriteByte(flag);
        expected.WriteInt(7);
        expected.WriteByte(0);
        return expected.ToArray();
    }

    // The flag after the two longs is the one whose false value makes the client refuse to destroy, trade,
    // list or repack the item; the server always sends it set.
    [Test]
    public void WriteTo_FlagIsAlwaysSet() {
        var enchant = new ItemEnchant(enchants: 3, enchantExp: 5, enchantCharges: 2, charges: 7);
        var writer = new ByteWriter();
        enchant.WriteTo(writer);

        Assert.That(writer.ToArray(), Is.EqualTo(Bytes(1)));
    }

    [Test]
    public void ReadFrom_DoesNotKeepAClearedFlag() {
        var enchant = new ItemEnchant();
        enchant.ReadFrom(new ByteReader(Bytes(0), 0));
        Assert.That(enchant.Enchants, Is.EqualTo(3));
        Assert.That(enchant.Charges, Is.EqualTo(7));

        var writer = new ByteWriter();
        enchant.WriteTo(writer);
        Assert.That(writer.ToArray(), Is.EqualTo(Bytes(1)));
    }
}
