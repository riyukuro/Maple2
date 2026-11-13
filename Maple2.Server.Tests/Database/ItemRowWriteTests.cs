using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Database;

// A save skips an item row whose values the item object last stored (ItemSaveTests); every other write of the row
// forgets them, so the next save writes it. Without that a split merged back, or a cube's join to its storage stack
// placed again, left the row at the other write's amount: an item lost on relog, or a furnishing doubled. Each test
// changes the row behind the server after the other write, so the row shows whether the next save wrote it.
// The in-memory store (no MySQL).
public class ItemRowWriteTests {
    private const int Stackable = 50000321, CubeItem = 50000322;
    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private int? Amount(long uid) {
        using var context = new Ms2Context(test.Options);
        return context.Item.AsNoTracking().SingleOrDefault(row => row.Id == uid)?.Amount;
    }

    private void SetAmount(long uid, int amount) {
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.Item row = context.Item.Single(item => item.Id == uid);
        row.Amount = amount;
        context.SaveChanges();
    }

    private bool Save(long ownerId, Item[] items, Item[]? discard = null) {
        using GameStorage.Request db = test.Storage.Context();
        return db.SaveItems(ownerId, items, discard ?? []);
    }

    // The case the rule rests on: an unchanged item is not written again (the row changed behind the server stays).
    [Test]
    public void UnchangedItem_IsNotWrittenAgain() {
        Item item = test.Store(TestGameSession.Metadata(Stackable), 10);
        Assume.That(Save(test.CharacterId, [item]), Is.True);
        SetAmount(item.Uid, 77);
        Assert.That(Save(test.CharacterId, [item]), Is.True);
        Assert.That(Amount(item.Uid), Is.EqualTo(77));
    }

    // A stack of 10 saved, 3 split off (the source row goes to 7), the 3 merged back: memory holds 10 again, the
    // values last stored; the save writes 10 all the same.
    [Test]
    public void SplitMergedBack_IsWritten() {
        Item stack = test.Store(TestGameSession.Metadata(Stackable), 10);
        Assume.That(Save(test.CharacterId, [stack]), Is.True);
        Item? piece;
        using (GameStorage.Request db = test.Storage.Context()) {
            piece = db.SplitItem(test.CharacterId, stack, 3);
        }
        Assume.That(piece, Is.Not.Null);
        Assume.That(Amount(stack.Uid), Is.EqualTo(7));

        Assert.That(Save(test.CharacterId, [stack], [piece!]), Is.True); // stack.Amount is 10 again
        Assert.Multiple(() => {
            Assert.That(Amount(stack.Uid), Is.EqualTo(10));
            Assert.That(Amount(piece!.Uid), Is.EqualTo(3), "the piece's row goes to owner 0");
        });
    }

    private static PlotInfo Info() => new(new UgcMapGroup(1, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0),
        new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0))) { Id = 4243, MapId = 62000000, Number = 1 };

    // A storage stack saved at 4; a placed cube goes back by a plot save and joins it (the row goes to 5); the cube
    // is placed again, so memory holds 4, the values last stored: the save writes 4 all the same.
    [Test]
    public void CubeJoinPlacedAgain_IsWritten() {
        ItemMetadata metadata = TestGameSession.Metadata(CubeItem);
        test.Register(metadata);
        Item stack;
        using (GameStorage.Request db = test.Storage.Context()) {
            stack = db.CreateItem(test.AccountId, new Item(metadata, amount: 4) { Group = ItemGroup.Furnishing })!;
        }
        Assume.That(stack, Is.Not.Null);
        Assume.That(Save(test.AccountId, [stack]), Is.True);
        PlotCube cube;
        using (GameStorage.Request db = test.Storage.Context()) {
            cube = db.CreateCube(Info(), new PlotCube(metadata, 0) { Type = PlotCube.CubeType.Default, Position = new Vector3B(1, 2, 3) })!;
        }
        Assume.That(cube, Is.Not.Null);

        using (GameStorage.Request db = test.Storage.Context()) {
            CubeReturn Join(System.Collections.Generic.IReadOnlyList<PlotCube> cubes) {
                var plan = new CubeReturn { AccountId = test.AccountId, CharacterId = test.CharacterId };
                plan.Joins.Add(new CubeReturn.Join(stack, stack.Amount + cubes.Count, 0));
                return plan;
            }
            Assume.That(db.SaveCubes(Info(), [], test.CharacterId, returns: Join), Is.Not.Null);
        }
        Assume.That(Amount(stack.Uid), Is.EqualTo(5));

        Assert.That(Save(test.AccountId, [stack]), Is.True); // placed again: stack.Amount is 4
        Assert.That(Amount(stack.Uid), Is.EqualTo(4));
    }

    // Two objects of one row (a home's cube and a copy): a save of the other one moves the row, so this one's next
    // save writes it although its own values are unchanged.
    [Test]
    public void SaveOfAnotherObject_OfTheRow_IsNotSkippedOver() {
        Item item = test.Store(TestGameSession.Metadata(Stackable), 10);
        Item copy = item.Clone();
        Assume.That(Save(test.CharacterId, [item]), Is.True);
        copy.Amount = 6;
        Assume.That(Save(test.CharacterId, [copy]), Is.True);
        Assert.That(Save(test.CharacterId, [item]), Is.True);
        Assert.That(Amount(item.Uid), Is.EqualTo(10));
    }

    // Two saves of one object that overlap: the one in a transaction ends after the other was kept, so the order the
    // row was written in is not known; neither is kept, and the next save writes the row, even with the values one of
    // them wrote.
    [Test]
    public void OverlappingSaves_KeepNothing() {
        Item item = test.Store(TestGameSession.Metadata(Stackable), 10);
        using (GameStorage.Request slow = test.Storage.Context()) {
            slow.BeginTransaction();
            item.Amount = 8;
            Assume.That(slow.SaveItems(test.CharacterId, item), Is.True);
            item.Amount = 9;
            Assume.That(Save(test.CharacterId, [item]), Is.True);
            item.Amount = 8;
            Assume.That(slow.Commit(), Is.True);
        }
        // 8, the values the slow save wrote: had it kept them, the row changed behind the server would stay.
        SetAmount(item.Uid, 77);
        Assert.That(Save(test.CharacterId, [item]), Is.True);
        Assert.That(Amount(item.Uid), Is.EqualTo(8));
    }

    // UpdateItem (a blueprint's confirmed picture) writes the item's values on the row it has: the row keeps its owner.
    [Test]
    public void UpdateItem_KeepsTheOwner() {
        Item item = test.Store(TestGameSession.Metadata(Stackable), 10);
        item.Amount = 6;
        using (GameStorage.Request db = test.Storage.Context()) {
            Assert.That(db.UpdateItem(item), Is.True);
        }
        using var context = new Ms2Context(test.Options);
        Maple2.Database.Model.Item row = context.Item.AsNoTracking().Single(r => r.Id == item.Uid);
        Assert.Multiple(() => {
            Assert.That(row.Amount, Is.EqualTo(6));
            Assert.That(row.OwnerId, Is.EqualTo(test.CharacterId));
        });
    }

    // A transaction rolled back after its save (or a commit whose answer was lost): nothing is kept.
    [Test]
    public void RolledBackSave_KeepsNothing() {
        Item item = test.Store(TestGameSession.Metadata(Stackable), 10);
        using (GameStorage.Request db = test.Storage.Context()) {
            db.BeginTransaction();
            Assume.That(db.SaveItems(test.CharacterId, item), Is.True);
            db.Rollback();
        }
        SetAmount(item.Uid, 77);
        Assert.That(Save(test.CharacterId, [item]), Is.True);
        Assert.That(Amount(item.Uid), Is.EqualTo(10));
    }
}
