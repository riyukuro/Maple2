using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The enchant transform's outputs: each one reaches the bag or the mail.
public class TransferEnchantTests {
    private const int GearId = 11300001;
    private const int OutputId = 20000004;
    private TestGameSession test = null!;

    // The bag's write of the output fails: the output comes by mail and the result is sent. Before, the output's
    // row was made first, and its failed write threw after the gear and the meso were taken.
    [Test]
    public void FailedBagWrite_MailsTheOutput() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(GearId, EquipSlot.CP, slotMax: 1), TestGameSession.Metadata(OutputId));
        GameSession session = test.Start();
        // Items are owned by a character, account or mail id; the in-memory database starts each at 1, so one mail
        // id is used up first (see BagSplitTests.Mail).
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
        Item gear = session.Field!.ItemDrop.CreateItem(GearId)!;
        Assert.That(session.Item.Inventory.Add(gear, commit: true), Is.True);
        gear = session.Item.Inventory.Find(GearId).Single();
        test.SetTable(new ItemEnchantTransformTable([
            new ItemEnchantTransformTable.Entry(gear.Rarity, 0, 0, [(int) gear.Type.Type], [GearId], 100,
                [new ItemEnchantTransformTable.Output(OutputId, 1, 3)]),
        ]));
        session.Currency.Meso = 1000;
        test.TakeSent();
        // The first new item row (the bag's) fails; the mail's goes through.
        int failed = 0;
        test.FailSave = context => context.ChangeTracker.Entries().Any(entry => entry.State == EntityState.Added
                                                                               && entry.Entity.GetType().Name == "Item")
                                   && failed++ == 0;

        new TransferEnchantHandler { TableMetadata = test.TableMetadata }
            .Handle(session, TestGameSession.Packet(writer => writer.WriteLong(gear.Uid)));
        test.FailSave = null;

        using GameStorage.Request reload = test.Storage.Context();
        int mailed = reload.GetAllMail(test.CharacterId).SelectMany(mail => mail.Items).Where(item => item.Id == OutputId).Sum(item => item.Amount);
        using var context = new Ms2Context(test.Options);
        Assert.Multiple(() => {
            Assert.That(failed, Is.GreaterThan(1));
            Assert.That(mailed, Is.EqualTo(3));
            Assert.That(context.Item.Count(row => row.ItemId == OutputId && row.OwnerId == 0), Is.Zero);
            Assert.That(session.Item.Inventory.Find(GearId), Is.Empty);
            Assert.That(session.Currency.Meso, Is.EqualTo(900));
            Assert.That(test.TakeSentOps(), Does.Contain(SendOp.TransferEnchant));
        });
    }
}
