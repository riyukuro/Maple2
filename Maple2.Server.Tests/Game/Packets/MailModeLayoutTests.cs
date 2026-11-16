using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Tests.Game.Packets;

// The Mail packet read as CSceneStateGame::OnMail (scenestategame.cpp:11496) and its readers read it: mode 0 an int count,
// then CMailInfo::Read (mailattachitem.cpp:590; Ghidra FUN_1406913e0): byte kind, int64 id, int64 sender, five unicode
// strings, a byte attachment count with CMailAttachItem::CMailAttachItem per item (int id, int64 uid, byte slot, int
// rarity, int amount, int64, int, int64, then the item), three (int64, int64) ranges (the mesos, the merets and the
// game merets with their collect times: FUN_140693300), the benefit list (FUN_14026a3d0: a byte count), three int64
// (read, expiry, send time) and a unicode string; mode 2 and 0xb two int64; 0xa an int64, two bytes and an int64; 0xd an
// int64; 0xe an int, a bool and an int (ReadMailNew); 0x14 two bytes; 0x16 (ReadGiftNew) a unicode sender, a byte, an
// int item id, a byte rarity, an int amount, an ansi url and a unicode title; 1, 0xf, 0x10 and 0x11 read nothing.
public class MailModeLayoutTests {
    private static Item Attachment() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(20000001, "20000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        return new Item(metadata, 3, 5) { Uid = 0x0102030405060708, Rarity = 3 };
    }

    private static ByteReader Open(ByteWriter writer, byte mode) {
        var reader = new ByteReader(writer.ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.Mail));
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        return reader;
    }

    [Test]
    public void LoadIsACountAndTheMailInfoOfEachMail() {
        Item item = Attachment();
        var mail = new Mail { Type = MailType.System, Id = 9, SenderId = 4, SenderName = "Snd", Title = "Ti", Content = "Co", Meso = 100, MesoCollectTime = 7,
            Meret = 5, MeretCollectTime = 6, GameMeret = 3, GameMeretCollectTime = 2, ReadTime = 11, ExpiryTime = 12, SendTime = 13, WeddingInvite = "w" };
        mail.Items.Add(item);
        var stream = new ByteWriter();
        stream.WriteClass<Item>(item);
        byte[] body = stream.ToArray();

        ByteReader reader = Open(MailPacket.Load([mail]), 0);
        Assert.That(reader.ReadInt(), Is.EqualTo(1));
        Assert.That(reader.ReadByte(), Is.EqualTo((byte) MailType.System));
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(reader.ReadLong(), Is.EqualTo(4));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Snd"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Ti"));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Co"));
        reader.ReadUnicodeString(); // title args
        reader.ReadUnicodeString(); // content args
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(item.Id));
        Assert.That(reader.ReadLong(), Is.EqualTo(item.Uid));
        Assert.That(reader.ReadByte(), Is.Zero); // slot of the attachment
        Assert.That(reader.ReadInt(), Is.EqualTo(3)); // rarity
        Assert.That(reader.ReadInt(), Is.EqualTo(item.Amount));
        reader.ReadLong();
        reader.ReadInt();
        reader.ReadLong();
        Assert.That(reader.ReadBytes(body.Length), Is.EqualTo(body));
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((100L, 7L)));
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((5L, 6L)));
        Assert.That((reader.ReadLong(), reader.ReadLong()), Is.EqualTo((3L, 2L)));
        Assert.That(reader.ReadByte(), Is.Zero); // benefits
        Assert.That((reader.ReadLong(), reader.ReadLong(), reader.ReadLong()), Is.EqualTo((11L, 12L, 13L)));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("w"));
        Assert.That(reader.Available, Is.Zero);
    }

    [Test]
    public void ReadCollectAndDeleteModes() {
        var mail = new Mail { Type = MailType.System, Id = 9, ReadTime = 11 };

        ByteReader read = Open(MailPacket.Read(mail), 2);
        Assert.That((read.ReadLong(), read.ReadLong()), Is.EqualTo((9L, 11L)));
        Assert.That(read.Available, Is.Zero);

        ByteReader collectRead = Open(MailPacket.CollectRead(mail), 11);
        Assert.That((collectRead.ReadLong(), collectRead.ReadLong()), Is.EqualTo((9L, 11L)));
        Assert.That(collectRead.Available, Is.Zero);

        foreach ((ByteWriter writer, byte slotFlag, byte slot) in new[] {
                     (MailPacket.CollectItem(9, 3, 77), (byte) 1, (byte) 3),
                     (MailPacket.CollectMeso(9, 77), (byte) 0, (byte) 0),
                 }) {
            ByteReader reader = Open(writer, 10);
            Assert.That(reader.ReadLong(), Is.EqualTo(9));
            Assert.That(reader.ReadByte(), Is.EqualTo(slotFlag));
            Assert.That(reader.ReadByte(), Is.EqualTo(slot));
            Assert.That(reader.ReadLong(), Is.EqualTo(77));
            Assert.That(reader.Available, Is.Zero);
        }

        ByteReader deleted = Open(MailPacket.Deleted(9), 13);
        Assert.That(deleted.ReadLong(), Is.EqualTo(9));
        Assert.That(deleted.Available, Is.Zero);
    }

    [Test]
    public void NotifyErrorGiftAndTheBodylessModes() {
        ByteReader notify = Open(MailPacket.Notify(3, true, 2), 14);
        Assert.That((notify.ReadInt(), notify.ReadBool(), notify.ReadInt()), Is.EqualTo((3, true, 2)));
        Assert.That(notify.Available, Is.Zero);

        ByteReader error = Open(MailPacket.Error(MailError.s_mail_error_username, 4), 20);
        Assert.That(error.ReadByte(), Is.EqualTo(4));
        Assert.That(error.ReadByte(), Is.EqualTo((byte) MailError.s_mail_error_username));
        Assert.That(error.Available, Is.Zero);

        ByteReader gift = Open(MailPacket.Gift("Snd", 20000001, 3, 5, "url", "Ti"), 22);
        Assert.That(gift.ReadUnicodeString(), Is.EqualTo("Snd"));
        gift.ReadByte();
        Assert.That(gift.ReadInt(), Is.EqualTo(20000001));
        Assert.That(gift.ReadByte(), Is.EqualTo(3));
        Assert.That(gift.ReadInt(), Is.EqualTo(5));
        Assert.That(gift.ReadString(), Is.EqualTo("url"));
        Assert.That(gift.ReadUnicodeString(), Is.EqualTo("Ti"));
        Assert.That(gift.Available, Is.Zero);

        Assert.That(Open(MailPacket.Send(), 1).Available, Is.Zero);
        Assert.That(Open(MailPacket.NotifyTemporary(), 15).Available, Is.Zero);
        Assert.That(Open(MailPacket.StartList(), 16).Available, Is.Zero);
        Assert.That(Open(MailPacket.EndList(), 17).Available, Is.Zero);
    }
}
