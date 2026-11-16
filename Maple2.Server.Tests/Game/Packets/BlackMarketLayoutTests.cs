using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// BlackMarket against CBlackMarketMan::OnBlackMarket (blackmarketman.cpp:117): u8 mode; 1 (own list) and 4
// (search): int count x CBlackMarketProduct::Read (commonfiles/blackmarketitemoption.cpp:564: long id, long created,
// long created again, long expiry, int count, int bought, long unit price, u8 state, long item uid, int item id, u8
// rank, long owner, the item stream CItem::ReadFromStream); 2: one product; 3: long id, u8 removed-already; 5: long id,
// int bought; 7: none; 8: int, int, long (FUN_140a21be0); 9: none. The item stream itself is read back against
// CItem::ReadFromStream in ItemLayoutTests; here it is compared as the bytes the shared Item writer produces.
public class BlackMarketLayoutTests {
    private static ByteReader Read(ByteWriter writer) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.BlackMarket));
        return reader;
    }

    private static BlackMarketListing Listing() {
        var item = new Item(TestGameSession.Metadata(20000001), 3, 5) { Uid = 7001 };
        return new BlackMarketListing(item) {
            Id = 42, CreationTime = 1700000000, ExpiryTime = 1700086400, Price = 9000, AccountId = 11, CharacterId = 12,
        };
    }

    private static void ReadProduct(ByteReader reader) {
        Assert.That(reader.ReadLong(), Is.EqualTo(42));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700000000));
        Assert.That(reader.ReadLong(), Is.EqualTo(1700086400));
        Assert.That(reader.ReadInt(), Is.EqualTo(5));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(9000));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(7001));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        var expected = new ByteWriter();
        expected.WriteClass<Item>(Listing().Item);
        Assert.That(reader.ReadBytes(reader.Available), Is.EqualTo(expected.ToArray()));
    }

    [Test]
    public void Lists_AndAdd() {
        ByteReader reader = Read(BlackMarketPacket.MyListings(new List<BlackMarketListing> { Listing() }));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        ReadProduct(reader);

        reader = Read(BlackMarketPacket.Search(new List<BlackMarketListing> { Listing() }));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        ReadProduct(reader);

        reader = Read(BlackMarketPacket.Add(Listing()));
        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        ReadProduct(reader);
    }

    [Test]
    public void ShortModes() {
        ByteReader reader = Read(BlackMarketPacket.Remove(42, true));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(42));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BlackMarketPacket.Purchase(42, 3));
        Assert.That(reader.ReadByte(), Is.EqualTo(5));
        Assert.That(reader.ReadLong(), Is.EqualTo(42));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BlackMarketPacket.PurchaseResponse());
        Assert.That(reader.ReadByte(), Is.EqualTo(7));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BlackMarketPacket.Preview(20000001, 3, 500));
        Assert.That(reader.ReadByte(), Is.EqualTo(8));
        Assert.That(reader.ReadInt(), Is.EqualTo(20000001));
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(500));
        Assert.That(reader.Available, Is.EqualTo(0));

        reader = Read(BlackMarketPacket.Open());
        Assert.That(reader.ReadByte(), Is.EqualTo(9));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
