using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The maid row of a maid cube through GameStorage, on an in-memory database: made only for a placed cube, read back
// with its values, deleted with the cube, and moved to the storage item and back when the cube is picked up and
// placed again.
// The times are real ones: the row keeps them as local DateTimes, and one before the epoch reads back as 0.
public class MaidStorageTests {
    private const int CubeItemId = 50400001;
    private const long PlotId = 7001;
    private GameStorage storage = null!;

    [SetUp]
    public void SetUp() {
        DbContextOptions options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        storage = new GameStorage(options, Storage(Metadata()), null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private static ItemMetadataStorage Storage(params ItemMetadata[] items) {
        var itemStorage = (ItemMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ItemMetadataStorage));
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        foreach (ItemMetadata item in items) {
            cache.AddReplace(item.Id, item);
        }
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(itemStorage, cache);
        return itemStorage;
    }

    private static ItemMetadata Metadata() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(CubeItemId, null, [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [],
            null, null, new ItemMetadataHousing(0, 0, 0, default, true, 3), null);
    }

    private PlotCube PlaceCube() {
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = PlotId;
        var cube = new PlotCube(Metadata()) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction };
        using GameStorage.Request db = storage.Context();
        return db.CreateCube(plot, cube)!;
    }

    [Test]
    public void Create_IsReadBack() {
        PlotCube cube = PlaceCube();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMaid(new Maid {
                Uid = cube.Id, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000,
                Profile = new MaidProfile { Name = "n" },
            }), Is.Not.Null);
        }

        using GameStorage.Request check = storage.Context();
        Maid? maid = check.GetMaid(cube.Id);
        Assert.That(maid, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(maid!.OwnerId, Is.EqualTo(5));
            Assert.That(maid.ItemId, Is.EqualTo(CubeItemId));
            Assert.That(maid.MaidId, Is.EqualTo(3));
            Assert.That(maid.Level, Is.EqualTo(1));
            Assert.That(maid.CreationTime, Is.EqualTo(1700000000));
            Assert.That(maid.ExpiryTime, Is.EqualTo(1702592000));
            Assert.That(maid.Profile.Name, Is.EqualTo("n"));
        });
    }

    [Test]
    public void Create_WithoutTheCubeRow_IsRefused() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.CreateMaid(new Maid { Uid = 99, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Null);
        Assert.That(db.GetMaid(99), Is.Null);
    }

    [Test]
    public void DeleteCube_DeletesTheMaid() {
        PlotCube cube = PlaceCube();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMaid(new Maid { Uid = cube.Id, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Not.Null);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCube(cube), Is.True);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetMaid(cube.Id), Is.Null);
    }

    [Test]
    public void SecondCreate_FailsAndTheFirstIsReadAgain() {
        PlotCube cube = PlaceCube();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMaid(new Maid { Uid = cube.Id, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Not.Null);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMaid(new Maid { Uid = cube.Id, OwnerId = 6, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Null);
        }

        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetMaid(cube.Id)?.OwnerId, Is.EqualTo(5));
    }

    [Test]
    public void Save_WritesMoodAffinityExpiryAndProfile_AndGetMaidsListsTheOwners() {
        PlotCube cube = PlaceCube();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.CreateMaid(new Maid { Uid = cube.Id, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Not.Null);
        }
        using (GameStorage.Request db = storage.Context()) {
            Maid maid = db.GetMaid(cube.Id)!;
            maid.Mood = 40;
            maid.Level = 3;
            maid.Exp = 12;
            maid.ExpiryTime = 1705184000;
            maid.Profile = new MaidProfile { Name = "m", Hobby = "h" };
            Assert.That(db.SaveMaid(maid), Is.True);
        }

        using GameStorage.Request check = storage.Context();
        IList<Maid> maids = check.GetMaids(5);
        Assert.Multiple(() => {
            Assert.That(maids, Has.Count.EqualTo(1));
            Assert.That(maids[0].Mood, Is.EqualTo(40));
            Assert.That(maids[0].Level, Is.EqualTo(3));
            Assert.That(maids[0].Exp, Is.EqualTo(12));
            Assert.That(maids[0].ExpiryTime, Is.EqualTo(1705184000));
            Assert.That(maids[0].Profile.Name, Is.EqualTo("m"));
            Assert.That(maids[0].Profile.Hobby, Is.EqualTo("h"));
            Assert.That(check.GetMaids(6), Is.Empty);
        });
    }

    [Test]
    public void Save_OfAGoneRow_IsFalse() {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveMaid(new Maid { Uid = 99, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.False);
    }

    private const long StoredUid = 900001; // a made-up storage item uid

    private void Hire(long uid, int mood = 0) {
        using GameStorage.Request db = storage.Context();
        Assert.That(db.CreateMaid(new Maid { Uid = uid, OwnerId = 5, ItemId = CubeItemId, MaidId = 3, Mood = mood, CreationTime = 1700000000, ExpiryTime = 1702592000 }), Is.Not.Null);
    }

    [Test]
    public void Pickup_MovesTheMaidToTheItem_AndPlacementMovesItToTheNewCube() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id, mood: 40);

        // Pickup: the row moves to the storage item before the cube row is deleted.
        using (GameStorage.Request db = storage.Context()) {
            Maid? moved = db.MoveMaid(cube.Id, StoredUid);
            Assert.That(moved?.Uid, Is.EqualTo(StoredUid));
            Assert.That(db.DeleteCube(cube), Is.True);
        }
        using (GameStorage.Request check = storage.Context()) {
            Assert.That(check.GetMaid(cube.Id), Is.Null);
            Assert.That(check.GetMaid(StoredUid)?.Mood, Is.EqualTo(40));
            Assert.That(check.GetMaids(5).Select(maid => maid.Uid), Is.EqualTo(new[] { StoredUid }));
        }

        // Placement from that item: the row moves to the new cube with its values.
        PlotCube placed = PlaceCube();
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.MoveMaid(StoredUid, placed.Id)?.Uid, Is.EqualTo(placed.Id));
        }
        using GameStorage.Request after = storage.Context();
        Maid? maid = after.GetMaid(placed.Id);
        Assert.Multiple(() => {
            Assert.That(after.GetMaid(StoredUid), Is.Null);
            Assert.That(maid?.Mood, Is.EqualTo(40));
            Assert.That(maid?.CreationTime, Is.EqualTo(1700000000));
            Assert.That(maid?.ExpiryTime, Is.EqualTo(1702592000));
        });
    }

    [Test]
    public void SetCraft_WritesTheCraft_AndAMoveKeepsIt() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SetMaidCraft(cube.Id, 0, 0, 4101, 1700000500, 3600), Is.True);
            Assert.That(db.MoveMaid(cube.Id, StoredUid)?.CraftRecipeId, Is.EqualTo(4101));
        }
        using GameStorage.Request check = storage.Context();
        Maid? stored = check.GetMaid(StoredUid);
        Assert.Multiple(() => {
            Assert.That(stored?.CraftRecipeId, Is.EqualTo(4101));
            Assert.That(stored?.CraftStartTime, Is.EqualTo(1700000500));
            Assert.That(stored?.CraftDuration, Is.EqualTo(3600));
            Assert.That(check.GetMaidCraft(StoredUid), Is.EqualTo((4101, 1700000500L, 3600)));
            Assert.That(check.GetMaidCraft(cube.Id), Is.Null);
        });
    }

    // The home open in two channels: each holds its own copy of the maid, read while the craft ran. The first
    // receive clears the stored craft; the second, from its stale copy, changes nothing, so it gives no product.
    [Test]
    public void SetCraft_SecondReceiveFromAStaleCopy_ChangesNothing() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SetMaidCraft(cube.Id, 0, 0, 4101, 1700000500, 3600), Is.True);
        }
        Maid first, second;
        using (GameStorage.Request db = storage.Context()) {
            first = db.GetMaid(cube.Id)!;
        }
        using (GameStorage.Request db = storage.Context()) {
            second = db.GetMaid(cube.Id)!;
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SetMaidCraft(first.Uid, first.CraftRecipeId, first.CraftStartTime, 0, 0, 0), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SetMaidCraft(second.Uid, second.CraftRecipeId, second.CraftStartTime, 0, 0, 0), Is.False);
            Assert.That(db.GetMaidCraft(cube.Id), Is.EqualTo((0, 0L, 0)));
        }
    }

    // Two starts from copies without a craft: only the first is stored; the second reads the first's craft back.
    // A cancel or start from a copy whose craft is not the stored one changes nothing either.
    [Test]
    public void SetCraft_OnlyFromTheStoredCraft() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id);
        using GameStorage.Request db = storage.Context();
        Assert.Multiple(() => {
            Assert.That(db.SetMaidCraft(cube.Id, 0, 0, 4101, 1700000500, 3600), Is.True);
            Assert.That(db.SetMaidCraft(cube.Id, 0, 0, 4102, 1700000600, 60), Is.False);
            Assert.That(db.SetMaidCraft(cube.Id, 4101, 1700000400, 0, 0, 0), Is.False);
            Assert.That(db.SetMaidCraft(cube.Id, 4102, 1700000500, 0, 0, 0), Is.False);
            Assert.That(db.GetMaidCraft(cube.Id), Is.EqualTo((4101, 1700000500L, 3600)));
            Assert.That(db.SetMaidCraft(99, 0, 0, 4101, 1700000500, 3600), Is.False);
        });
    }

    // The general save writes the mood and affinity but not the craft: a copy read before a craft started does not
    // clear it.
    [Test]
    public void Save_LeavesTheCraft() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id);
        Maid stale;
        using (GameStorage.Request db = storage.Context()) {
            stale = db.GetMaid(cube.Id)!;
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SetMaidCraft(cube.Id, 0, 0, 4101, 1700000500, 3600), Is.True);
        }
        stale.Mood = 55;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveMaid(stale), Is.True);
        }
        using GameStorage.Request check = storage.Context();
        Assert.Multiple(() => {
            Assert.That(check.GetMaid(cube.Id)?.Mood, Is.EqualTo(55));
            Assert.That(check.GetMaidCraft(cube.Id), Is.EqualTo((4101, 1700000500L, 3600)));
        });
    }

    [Test]
    public void MoveMaid_WithoutAMaid_OrOntoATakenUid_IsRefused() {
        PlotCube first = PlaceCube();
        PlotCube second = PlaceCube();
        Hire(first.Id, mood: 10);
        Hire(second.Id, mood: 20);

        using GameStorage.Request db = storage.Context();
        Assert.Multiple(() => {
            Assert.That(db.MoveMaid(StoredUid, first.Id), Is.Null);
            Assert.That(db.MoveMaid(first.Id, second.Id), Is.Null);
            Assert.That(db.GetMaid(first.Id)?.Mood, Is.EqualTo(10));
            Assert.That(db.GetMaid(second.Id)?.Mood, Is.EqualTo(20));
        });
    }

    [Test]
    public void DeleteMaid_RemovesAStoredMaid() {
        PlotCube cube = PlaceCube();
        Hire(cube.Id);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.MoveMaid(cube.Id, StoredUid), Is.Not.Null);
            Assert.That(db.DeleteMaid(StoredUid), Is.True);
            Assert.That(db.DeleteMaid(StoredUid), Is.False);
        }
        using GameStorage.Request check = storage.Context();
        Assert.That(check.GetMaids(5), Is.Empty);
    }

    [Test]
    public void SaveCubes_DroppingACube_DropsItsMaid_ButNotAStoredOne() {
        PlotCube kept = PlaceCube();
        PlotCube dropped = PlaceCube();
        PlotCube picked = PlaceCube();
        Hire(dropped.Id);
        Hire(picked.Id);
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.MoveMaid(picked.Id, StoredUid), Is.Not.Null);
            Assert.That(db.DeleteCube(picked), Is.True);
        }

        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = PlotId;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveCubes(plot, [kept]), Is.Not.Null);
        }
        using GameStorage.Request check = storage.Context();
        Assert.Multiple(() => {
            Assert.That(check.GetMaid(dropped.Id), Is.Null);
            Assert.That(check.GetMaid(StoredUid), Is.Not.Null);
        });
    }

    [Test]
    public void StorageEntries_AMaidItemNeverStacks() {
        ItemMetadata maidItem = Metadata();
        var held = new Item(maidItem, amount: 1, initialize: false) { Uid = StoredUid };
        var emptied = new Item(maidItem, amount: 0, initialize: false) { Uid = StoredUid + 1 };
        Assert.Multiple(() => {
            Assert.That(MaidRules.CanJoin(held, CubeItemId, null, maidItem: true), Is.False);
            Assert.That(MaidRules.CanJoin(emptied, CubeItemId, null, maidItem: true), Is.True);
            Assert.That(MaidRules.CanJoin(held, CubeItemId, null, maidItem: false), Is.True);
            Assert.That(MaidRules.CanJoin(held, CubeItemId + 1, null, maidItem: false), Is.False);
        });
    }
}
