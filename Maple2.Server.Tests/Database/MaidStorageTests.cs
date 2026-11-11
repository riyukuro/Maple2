using System.Collections.Generic;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// The maid row of a maid cube through GameStorage, on an in-memory database: made only for a placed cube, read back
// with its values, and deleted with the cube.
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
}
