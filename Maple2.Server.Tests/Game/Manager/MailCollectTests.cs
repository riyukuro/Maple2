using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// Each attachment of a mail is given once (MailManager.Collect re-reads the mail from the database).
public class MailCollectTests {
    private const int MesoItem = 90000001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(MesoItem));
        return test.Start();
    }

    // A meso item on a mail: collected, its row leaves the mail at once, so collecting the mail again (a second
    // mode 11, or the mail list read back from the database) gives nothing more. The row went on the bag's delete
    // list only, still the mail's until the next character save, and every collect gave the mesos again.
    [Test]
    public void Collect_CurrencyItem_IsGivenOnce() {
        GameSession session = Start();
        StoredItem meso = session.Field!.ItemDrop.CreateItem(MesoItem)!;
        meso.Amount = 1000;
        long before = session.Currency.Meso;
        long mailId = Mail(session, meso);

        MailError first = session.Mail.Collect(mailId);
        MailError second = session.Mail.Collect(mailId);

        using GameStorage.Request reload = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(first, Is.EqualTo(MailError.none));
            Assert.That(second, Is.EqualTo(MailError.s_mail_error_already_receive));
            Assert.That(session.Currency.Meso - before, Is.EqualTo(1000));
            Assert.That(reload.GetMail(mailId, test.CharacterId)!.Items, Is.Empty);
        });
    }

    // A mail to the player with the items attached, in the player's inbox.
    private long Mail(GameSession session, params StoredItem[] items) {
        long mailId;
        using (GameStorage.Request db = test.Storage.Context()) {
            // Items are owned by a character, account or mail id; the in-memory database starts each range at 1, so
            // one mail id is used up first (as BagSplitTests).
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
        var mail = new Mail { ReceiverId = test.CharacterId, Type = MailType.System, Title = "t", Content = "c" };
        foreach (StoredItem item in items) {
            using GameStorage.Request db = test.Storage.Context();
            mail.Items.Add(db.CreateItem(0, item)!);
        }
        using (GameStorage.Request db = test.Storage.Context()) {
            mailId = db.CreateMail(mail)!.Id;
        }
        session.Mail.Notify(force: true);
        return mailId;
    }
}
