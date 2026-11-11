using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class UgcUploadHeaderTests {
    // An item upload (Ugc mode 1) as the client sent it, after the opcode and the mode byte.
    // Only the design name was replaced by one of the same length.
    private const string ItemUpload =
        "00 00 00 00 00 00 00 00 " + // upload control item uid
        "01 02 00 " + // kind, short
        "01 00 00 00 00 E4 0B 54 02 00 00 00 00 C8 17 A8 04 00 00 00 00 00 00 00 " + // user
        "00 00 00 00 00 00 00 00 00 00 00 FF 00 00 " + // upload
        "AB B1 74 5E 3F 48 00 00 A0 F5 AD 00 01 00 00 00 " + // item uid, item id, amount
        "08 00 74 00 73 00 68 00 69 00 72 00 74 00 30 00 31 00 " + // name
        "01 A0 86 01 00 00 00 00 00 00"; // rarity, price, voucher

    [Test]
    public void Read_StopsAtTheItemData() {
        byte[] data = Convert.FromHexString(ItemUpload.Replace(" ", ""));
        var reader = new ByteReader(data, 0);

        reader.ReadLong();
        UgcUploadHeader header = UgcUploadHeader.Read(reader);

        Assert.That(header.Type, Is.EqualTo(UgcType.Item));
        Assert.That(header.AccountId, Is.EqualTo(10000000000));
        Assert.That(header.CharacterId, Is.EqualTo(20000000000));
        Assert.That(header.UgcUid, Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0x483F5E74B1AB));
        Assert.That(reader.ReadInt(), Is.EqualTo(11400608));
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("tshirt01"));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadLong(), Is.EqualTo(100000));
        Assert.That(reader.ReadBool(), Is.False);
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
