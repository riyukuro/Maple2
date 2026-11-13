using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// RequestHome 3 with a model house (InitNewHome with a template) deletes the cubes already in the unnamed home; they
// go back to furnishing storage in the same save, and what storage does not take comes by mail. The template here
// is made up and has no cubes.
public class ModelHouseReturnTests {
    private const int CubeItem = 50400001;
    private const int OtherItem = 50400002;
    private const int MaidItem = 50400003;
    private TestGameSession test = null!;
    private ExportedUgcMapMetadata template = null!;

    private GameSession Start(int slotMax = 100) {
        test = new TestGameSession();
        GameSession session = test.Start();
        var cache = (Dictionary<string, ExportedUgcMapMetadata>) typeof(MapMetadataStorage)
            .GetField("ExportedUgcCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(test.MapMetadata)!;
        template = new ExportedUgcMapMetadata("1", new Vector3B(-3, -3, 0), [11, 11, 5], []);
        cache["1"] = template;
        test.Register(TestGameSession.Metadata(CubeItem, slotMax: slotMax));
        test.Register(TestGameSession.Metadata(OtherItem));
        test.Register(TestGameSession.Metadata(MaidItem, slotMax: 1) with { Housing = new ItemMetadataHousing(0, 0, 0, default, true, 3) });
        return session;
    }

    private PlotCube Place(GameSession session, int itemId, byte x) {
        session.ItemMetadata.TryGet(itemId, out ItemMetadata? metadata);
        using GameStorage.Request db = test.Storage.Context();
        PlotCube? cube = db.CreateCube(session.Player.Value.Home.Indoor, new PlotCube(metadata!) { Position = new Vector3B(x, 1, 0), Type = PlotCube.CubeType.Construction });
        Assert.That(cube, Is.Not.Null);
        return cube!;
    }

    private List<StoredItem> StoredRows(GameSession session) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetItemGroups(session.AccountId, ItemGroup.Furnishing).TryGetValue(ItemGroup.Furnishing, out List<StoredItem>? rows) ? rows : [];
    }

    private ICollection<Mail> Mails(GameSession session) {
        using GameStorage.Request db = test.Storage.Context();
        return db.GetAllMail(session.CharacterId);
    }

    [Test]
    public void DeletedCubes_GoBackToStorage_StackedByItem() {
        GameSession session = Start();
        Assert.That(session.Item.Furnishing.AddCube(CubeItem), Is.Not.Zero); // a stack of 1 already in storage
        Place(session, CubeItem, 1);
        Place(session, CubeItem, 2);
        Place(session, OtherItem, 3);

        session.Housing.InitNewHome("home", template);

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.HasCubes(session.Player.Value.Home.Indoor.Id), Is.False);
        }
        Assert.Multiple(() => {
            Assert.That(session.Item.Furnishing.GetItem(CubeItem)?.Amount, Is.EqualTo(3));
            Assert.That(session.Item.Furnishing.GetItem(OtherItem)?.Amount, Is.EqualTo(1));
            List<StoredItem> rows = StoredRows(session);
            Assert.That(rows.Where(row => row.Id == CubeItem).Select(row => row.Amount), Is.EqualTo(new[] { 3 }));
            Assert.That(rows.Where(row => row.Id == OtherItem).Select(row => row.Amount), Is.EqualTo(new[] { 1 }));
            Assert.That(Mails(session), Is.Empty);
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The stack the cubes would join is full (stack size 2): they come by mail, two to an item.
    [Test]
    public void CubesStorageDoesNotTake_AreMailed() {
        GameSession session = Start(slotMax: 2);
        session.Item.Furnishing.AddCube(CubeItem);
        session.Item.Furnishing.AddCube(CubeItem);
        Place(session, CubeItem, 1);
        Place(session, CubeItem, 2);
        Place(session, CubeItem, 3);

        session.Housing.InitNewHome("home", template);

        ICollection<Mail> mails = Mails(session);
        Assert.Multiple(() => {
            Assert.That(session.Item.Furnishing.GetItem(CubeItem)?.Amount, Is.EqualTo(2));
            Assert.That(mails, Has.Count.EqualTo(2));
            Assert.That(mails.Select(mail => mail.Type), Is.All.EqualTo(MailType.System));
            Assert.That(mails.SelectMany(mail => mail.Items).Select(item => (item.Id, item.Amount)).OrderBy(entry => entry.Amount),
                Is.EqualTo(new[] { (CubeItem, 1), (CubeItem, 2) }));
        });
        using GameStorage.Request db = test.Storage.Context();
        Assert.That(db.HasCubes(session.Player.Value.Home.Indoor.Id), Is.False);
    }

    // The save that deletes the cubes fails: they stay in the home, storage and the mailbox are as they were.
    [Test]
    public void FailedSave_LosesNothing() {
        GameSession session = Start();
        session.Item.Furnishing.AddCube(CubeItem);
        PlotCube cube = Place(session, CubeItem, 1);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.UgcMapCube>().Any(entry => entry.State == EntityState.Deleted);

        session.Housing.InitNewHome("home", template);
        test.FailSave = null;

        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.GetCube(cube.Id), Is.Not.Null);
        }
        Assert.Multiple(() => {
            Assert.That(session.Item.Furnishing.GetItem(CubeItem)?.Amount, Is.EqualTo(1));
            Assert.That(StoredRows(session).Where(row => row.Id == CubeItem).Select(row => row.Amount), Is.EqualTo(new[] { 1 }));
            Assert.That(Mails(session), Is.Empty);
        });
    }

    // A maid cube goes back with its maid, now keyed by the storage item. With otherFirst a stored item takes the
    // first item id, so the new stack's id differs from the cube's; without it the two ids are the same.
    [TestCase(false)]
    [TestCase(true)]
    public void MaidCube_KeepsItsMaid(bool otherFirst) {
        GameSession session = Start();
        if (otherFirst) {
            session.Item.Furnishing.AddCube(OtherItem);
        }
        PlotCube cube = Place(session, MaidItem, 1);
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.CreateMaid(new Maid { Uid = cube.Id, OwnerId = session.AccountId, ItemId = MaidItem, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Not.Null);
        }

        session.Housing.InitNewHome("home", template);

        StoredItem? stored = session.Item.Furnishing.GetItem(MaidItem);
        Assert.That(stored?.Amount, Is.EqualTo(1));
        using GameStorage.Request check = test.Storage.Context();
        Assert.Multiple(() => {
            // Item ids and cube ids are counted apart: the new stack may have the cube's id.
            Assert.That(stored!.Uid == cube.Id, Is.EqualTo(!otherFirst));
            if (otherFirst) {
                Assert.That(check.GetMaid(cube.Id), Is.Null);
            }
            Assert.That(check.GetMaid(stored!.Uid)?.MaidId, Is.EqualTo(3));
        });
    }

    // A furnishing item from mail goes into storage whole; with no room it stays in the mail.
    [Test]
    public void MailedCubes_AreCollectedIntoStorage_OrLeftInTheMail() {
        GameSession session = Start(slotMax: 2);
        session.Item.Furnishing.AddCube(CubeItem);
        session.Item.Furnishing.AddCube(CubeItem);
        Place(session, CubeItem, 1);
        session.Housing.InitNewHome("home", template);
        Mail mail = Mails(session).Single();
        session.Mail.Notify(true);

        Assert.That(session.Mail.Collect(mail.Id), Is.EqualTo(MailError.s_mail_error_receiveitem_to_inven));
        Assert.That(Mails(session).Single().Items, Has.Count.EqualTo(1));

        StoredItem stack = session.Item.Furnishing.GetItem(CubeItem)!;
        session.Item.Furnishing.RemoveItem(stack.Uid); // the stack is emptied: room for the mailed cube
        Assert.That(session.Mail.Collect(mail.Id), Is.EqualTo(MailError.none));
        Assert.Multiple(() => {
            Assert.That(session.Item.Furnishing.GetItem(CubeItem)?.Amount, Is.EqualTo(1));
            Assert.That(Mails(session).Single().Items, Is.Empty);
        });
    }
}
