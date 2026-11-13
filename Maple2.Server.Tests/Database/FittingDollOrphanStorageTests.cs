using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A mannequin's item rows when its cube row goes: a plot save that drops the cube (an older instance of the home)
// mails each row back in its save, and the world start sweep finds rows left on a cube that no longer exists.
// The database is in memory, which ignores transactions: a save that fails after the mails were written keeps the
// mails here (on MySQL they roll back with it), so the failure test checks the item and cube rows only.
public class FittingDollOrphanStorageTests {
    private const int ItemId = 20000101;
    private const int CubeItemId = 50000101;
    private const long PlotId = 7001;
    private const long OtherPlotId = 7002;
    private const long Saver = 4242; // the character that saves the plot (no character row needed)
    private const long DeletedCharacter = 999_999;
    private const long GoneCube = 8_000_000_001; // the uid of a cube row that no longer exists

    private TestGameSession test = null!;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
        test.Register(TestGameSession.Metadata(ItemId), TestGameSession.Metadata(CubeItemId));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MailExpiryDays = 30 });
        storage = test.Storage;
    }

    private static PlotInfo Plot(long id) {
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = id;
        return plot;
    }

    private PlotCube PlaceCube(long plotId) {
        var cube = new PlotCube(TestGameSession.Metadata(CubeItemId)) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction };
        using GameStorage.Request db = storage.Context();
        return db.CreateCube(Plot(plotId), cube)!;
    }

    // An item row on the doll of cube uid ownerId, bound to boundTo when it is not 0.
    private long OnDoll(long ownerId, long boundTo = 0, EquipSlot slot = EquipSlot.CL) {
        Item item = test.Store(TestGameSession.Metadata(ItemId));
        item.Group = ItemGroup.FittingDoll;
        item.Slot = (short) slot;
        if (boundTo != 0) {
            item.Transfer = new ItemTransfer(TransferFlag.Bind, binding: new ItemBinding(boundTo, "n"));
        }
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveItems(ownerId, item), Is.True);
        return item.Uid;
    }

    private Maple2.Database.Model.Item Row(long uid) {
        using var check = new Ms2Context(test.Options);
        return check.Item.AsNoTracking().Single(row => row.Id == uid);
    }

    private List<Maple2.Database.Model.Mail> Mails() {
        using var check = new Ms2Context(test.Options);
        return check.Mail.AsNoTracking().ToList();
    }

    private bool CubeExists(long uid) {
        using var check = new Ms2Context(test.Options);
        return check.UgcMapCube.AsNoTracking().Any(cube => cube.Id == uid);
    }

    // Each row of the dropped doll goes into a mail of its own: to the bound character, else (unbound, or bound to a
    // deleted character) to the saving character. The row is in the mail's bag slot, and the cube row is gone.
    [Test]
    public void SaveCubes_DroppingADoll_MailsEachRowOnce() {
        PlotCube kept = PlaceCube(PlotId);
        PlotCube doll = PlaceCube(PlotId);
        long unbound = OnDoll(doll.Id, slot: EquipSlot.CL);
        long bound = OnDoll(doll.Id, test.CharacterId, EquipSlot.PA);
        long boundToDeleted = OnDoll(doll.Id, DeletedCharacter, EquipSlot.SH);
        long onKept = OnDoll(kept.Id);

        var mailed = new List<(long CharacterId, long MailId)>();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(PlotId), [kept], Saver, mailed), Is.Not.Null);
        }

        List<Maple2.Database.Model.Mail> mails = Mails();
        Dictionary<long, long> receiverOf = mails.ToDictionary(mail => mail.Id, mail => mail.ReceiverId);
        Assert.Multiple(() => {
            Assert.That(CubeExists(doll.Id), Is.False);
            Assert.That(CubeExists(kept.Id), Is.True);
            Assert.That(mails, Has.Count.EqualTo(3));
            Assert.That(mails.Select(mail => mail.Type), Is.All.EqualTo(MailType.System));
            Assert.That(mailed, Is.EquivalentTo(mails.Select(mail => (mail.ReceiverId, mail.Id))));
            foreach (long uid in new[] { unbound, bound, boundToDeleted }) {
                Maple2.Database.Model.Item row = Row(uid);
                Assert.That((row.Group, row.Slot), Is.EqualTo((ItemGroup.Default, (short) -1)));
                Assert.That(receiverOf.ContainsKey(row.OwnerId), Is.True);
            }
            Assert.That(receiverOf[Row(unbound).OwnerId], Is.EqualTo(Saver));
            Assert.That(receiverOf[Row(bound).OwnerId], Is.EqualTo(test.CharacterId));
            Assert.That(receiverOf[Row(boundToDeleted).OwnerId], Is.EqualTo(Saver));
            Assert.That(mails.Select(mail => mail.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That((Row(onKept).OwnerId, Row(onKept).Group), Is.EqualTo((kept.Id, ItemGroup.FittingDoll)));
        });

        // A second save of the same cubes finds nothing left to mail.
        mailed.Clear();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(PlotId), [kept], Saver, mailed), Is.Not.Null);
        }
        Assert.Multiple(() => {
            Assert.That(mailed, Is.Empty);
            Assert.That(Mails(), Has.Count.EqualTo(3));
        });
    }

    // The plot save fails: the cube stays and its item rows stay on it, in the doll group.
    [Test]
    public void SaveCubes_FailedSave_LeavesTheDollAndItsRows() {
        PlotCube kept = PlaceCube(PlotId);
        PlotCube doll = PlaceCube(PlotId);
        long uid = OnDoll(doll.Id, test.CharacterId);
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.UgcMapCube>().Any(entry => entry.State == EntityState.Deleted);

        var mailed = new List<(long CharacterId, long MailId)>();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(Plot(PlotId), [kept], Saver, mailed), Is.Null);
        }
        test.FailSave = null;

        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(mailed, Is.Empty);
            Assert.That(CubeExists(doll.Id), Is.True);
            Assert.That((row.OwnerId, row.Group, row.Slot), Is.EqualTo((doll.Id, ItemGroup.FittingDoll, (short) EquipSlot.CL)));
        });
    }

    // By default the sweep only counts and logs: no row moves and no mail is written.
    [Test]
    public void Sweep_ByDefault_OnlyCounts() {
        long bound = OnDoll(GoneCube, test.CharacterId);
        long unbound = OnDoll(GoneCube, slot: EquipSlot.PA);

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ReturnOrphanedDollItems(mail: false), Is.EqualTo((2, 0)));
        }
        Assert.Multiple(() => {
            Assert.That(Mails(), Is.Empty);
            Assert.That((Row(bound).OwnerId, Row(bound).Group), Is.EqualTo((GoneCube, ItemGroup.FittingDoll)));
            Assert.That((Row(unbound).OwnerId, Row(unbound).Group), Is.EqualTo((GoneCube, ItemGroup.FittingDoll)));
        });
    }

    // With mailing on, a row bound to an existing character is mailed to it, once; an unbound row, or one bound to a
    // deleted character, has no known owner and stays on the gone cube (still counted).
    [Test]
    public void Sweep_MailOn_MailsBoundRowsOnce_AndLeavesTheRest() {
        long bound = OnDoll(GoneCube, test.CharacterId);
        long unbound = OnDoll(GoneCube, slot: EquipSlot.PA);
        long boundToDeleted = OnDoll(GoneCube, DeletedCharacter, EquipSlot.SH);

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ReturnOrphanedDollItems(mail: true), Is.EqualTo((3, 1)));
        }
        Maple2.Database.Model.Mail mail = Mails().Single();
        Assert.Multiple(() => {
            Assert.That(mail.ReceiverId, Is.EqualTo(test.CharacterId));
            Assert.That((Row(bound).OwnerId, Row(bound).Group, Row(bound).Slot), Is.EqualTo((mail.Id, ItemGroup.Default, (short) -1)));
            Assert.That((Row(unbound).OwnerId, Row(unbound).Group), Is.EqualTo((GoneCube, ItemGroup.FittingDoll)));
            Assert.That((Row(boundToDeleted).OwnerId, Row(boundToDeleted).Group), Is.EqualTo((GoneCube, ItemGroup.FittingDoll)));
        });

        // The next start: the mailed row is not found again.
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ReturnOrphanedDollItems(mail: true), Is.EqualTo((2, 0)));
        }
        Assert.That(Mails(), Has.Count.EqualTo(1));
    }

    // A row whose owner uid still has a row anywhere is not an orphan: a cube on another plot, a cube of a saved
    // layout, an item (a cube in storage), a mail, a character or an account.
    [Test]
    public void Sweep_SkipsUidsThatStillExist() {
        PlotCube elsewhere = PlaceCube(OtherPlotId);
        long layoutCube;
        long mailId;
        using (var context = new Ms2Context(test.Options)) {
            var layout = new Maple2.Database.Model.HomeLayoutCube { HomeLayoutId = 1, ItemId = CubeItemId };
            context.UgcCubeLayout.Add(layout);
            context.SaveChanges();
            layoutCube = layout.Id;
        }
        using (GameStorage.Request db = storage.Context()) {
            mailId = db.CreateMail(new Mail { ReceiverId = test.CharacterId, Type = MailType.System })!.Id;
        }
        long storedCube = test.Store(TestGameSession.Metadata(CubeItemId)).Uid;
        long[] rows = [
            OnDoll(elsewhere.Id, test.CharacterId),
            OnDoll(layoutCube, test.CharacterId),
            OnDoll(storedCube, test.CharacterId),
            OnDoll(mailId, test.CharacterId),
            OnDoll(test.CharacterId, test.CharacterId),
            OnDoll(test.AccountId, test.CharacterId),
        ];

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ReturnOrphanedDollItems(mail: true), Is.EqualTo((0, 0)));
        }
        Assert.Multiple(() => {
            Assert.That(Mails(), Has.Count.EqualTo(1));
            Assert.That(rows.Select(uid => Row(uid).Group), Is.All.EqualTo(ItemGroup.FittingDoll));
        });
    }
}
