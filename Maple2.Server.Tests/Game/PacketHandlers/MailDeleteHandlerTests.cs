using System;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Mail 13: a deleted mail takes its uncollected items with it, and a locked item on it keeps it (the client's
// UIMailListDialog::HasLockedAttachment check).
public class MailDeleteHandlerTests {
    private const int ItemId = 20000005;

    private TestGameSession test = null!;
    private readonly MailHandler handler = new();

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    private (Mail Mail, Item Item) StoreMailWithItem(bool locked) {
        ItemMetadata metadata = TestGameSession.Metadata(ItemId);
        test.Register(metadata);
        using GameStorage.Request db = test.Storage.Context();
        Mail mail = db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c" })
                    ?? throw new InvalidOperationException("mail row not created");
        Item item = db.CreateItem(mail.Id, new Item(metadata, amount: 2) { IsLocked = locked })
                    ?? throw new InvalidOperationException("item row not created");
        return (mail, item);
    }

    private void Delete(GameSession session, long mailId) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(13);
            writer.WriteInt(1);
            writer.WriteLong(mailId);
        }));
    }

    private long? OwnerOf(long itemUid) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.AsNoTracking().FirstOrDefault(row => row.Id == itemUid)?.OwnerId;
    }

    [Test]
    public void Delete_ReleasesTheUncollectedItems() {
        test = new TestGameSession();
        (Mail mail, Item item) = StoreMailWithItem(locked: false);
        GameSession session = test.Start();
        test.TakeSent();

        Delete(session, mail.Id);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(MailPacket.Deleted(mail.Id)) }));
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetMail(mail.Id, test.CharacterId), Is.Null);
        }
        // Owner 0: the world server's start-up purge deletes the row.
        Assert.That(OwnerOf(item.Uid), Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    [Test]
    public void Delete_KeepsAMailWithALockedItem() {
        test = new TestGameSession();
        (Mail mail, Item item) = StoreMailWithItem(locked: true);
        GameSession session = test.Start();
        test.TakeSent();

        Delete(session, mail.Id);

        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.Mail), Is.Empty);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetMail(mail.Id, test.CharacterId)?.Items.Select(row => row.Uid), Is.EqualTo(new[] { item.Uid }));
        }
        Assert.That(OwnerOf(item.Uid), Is.EqualTo(mail.Id));
    }
}
