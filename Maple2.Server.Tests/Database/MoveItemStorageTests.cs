using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// GameStorage.MoveItem: a fitting doll's item row moves only while it is still where the caller's copy says it is.
public class MoveItemStorageTests {
    private const int ItemId = 20000101;
    private const long Character = 1001;
    private const long OtherCharacter = 1002;
    private const long Doll = 5001;
    private const int CubeItemId = 50000101;
    private const long PlotId = 7001;
    private GameStorage storage = null!;
    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, Storage(Metadata(ItemId, false), Metadata(CubeItemId, false)), null!, null!, null!, null!, null!,
            NullLogger<GameStorage>.Instance, null!);
    }

    // ItemMetadataStorage's constructor indexes pets with SQL the in-memory provider cannot run, so the storage is
    // made without it and the item data is put in its cache, where TryGet looks first.
    private static ItemMetadataStorage Storage(params ItemMetadata[] items) {
        var itemStorage = (ItemMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ItemMetadataStorage));
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        foreach (ItemMetadata item in items) {
            cache.AddReplace(item.Id, item);
        }
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(itemStorage, cache);
        return itemStorage;
    }

    private static ItemMetadata Metadata(int id, bool notInPlayedClient) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0) {
            NotInPlayedClient = notInPlayedClient,
        };
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    // Each step uses its own request, as the handler does.
    private Item Create() {
        using GameStorage.Request db = storage.Context();
        return db.CreateItem(Character, new Item(Metadata(ItemId, false)))!;
    }

    private long CreateOnDoll() {
        Item item = Create();
        item.Group = ItemGroup.FittingDoll;
        item.Slot = (short) EquipSlot.CL;
        using GameStorage.Request db = storage.Context();
        Assert.That(db.SaveItems(Doll, item), Is.True);
        return item.Uid;
    }

    private Item LoadFromDoll() {
        using GameStorage.Request db = storage.Context();
        return db.GetItemGroupsNoTracking(Doll, ItemGroup.FittingDoll)[ItemGroup.FittingDoll].Single();
    }

    private Maple2.Database.Model.Item Row(long uid) {
        using var check = new Ms2Context(options);
        return check.Item.AsNoTracking().Single(row => row.Id == uid);
    }

    // The same home open on two channels: each field loaded the doll's item. The first take-off moves the row, the
    // second finds it gone and gives nothing.
    [Test]
    public void TwoInstances_OnlyTheFirstTakeOffMovesTheRow() {
        long uid = CreateOnDoll();
        Item firstCopy = LoadFromDoll();
        Item secondCopy = LoadFromDoll();
        using GameStorage.Request first = storage.Context();
        using GameStorage.Request second = storage.Context();
        foreach (Item copy in new[] { firstCopy, secondCopy }) {
            copy.Group = ItemGroup.Default;
            copy.Slot = -1;
        }

        Assert.That(first.MoveItem(firstCopy, Doll, ItemGroup.FittingDoll, Character), Is.True);
        Assert.That(second.MoveItem(secondCopy, Doll, ItemGroup.FittingDoll, OtherCharacter), Is.False);
        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(Character));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.Default));
        });
    }

    private static PlotInfo Plot() {
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = PlotId;
        return plot;
    }

    private ItemMove PutOn(long cubeUid, long plotId, out long uid, ItemGroup savedGroup = ItemGroup.Default) {
        Item item = Create();
        uid = item.Uid;
        if (savedGroup != ItemGroup.Default) {
            item.Group = savedGroup;
            using GameStorage.Request save = storage.Context();
            Assert.That(save.SaveItems(Character, item), Is.True);
        }
        item.Group = ItemGroup.FittingDoll;
        item.Slot = (short) EquipSlot.CL;
        using GameStorage.Request db = storage.Context();
        return db.PutOnFittingDoll(item, cubeUid, plotId);
    }

    private PlotCube PlaceCube() {
        var cube = new PlotCube(Metadata(CubeItemId, false)) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction };
        using GameStorage.Request db = storage.Context();
        return db.CreateCube(Plot(), cube)!;
    }

    [Test]
    public void PutOn_MovesTheRowToTheDoll() {
        PlotCube cube = PlaceCube();
        Assert.That(PutOn(cube.Id, PlotId, out long uid), Is.EqualTo(ItemMove.Moved));
        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(cube.Id));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.FittingDoll));
        });
    }

    // The inventory is saved lazily: an item just taken off still has its row in the gear group until the next
    // character save. The put on does not test the row, so it is not refused.
    [Test]
    public void PutOn_ItemNotSavedToTheInventoryYet_IsMoved() {
        PlotCube cube = PlaceCube();
        Assert.That(PutOn(cube.Id, PlotId, out long uid, savedGroup: ItemGroup.Gear), Is.EqualTo(ItemMove.Moved));
        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(cube.Id));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.FittingDoll));
        });
    }

    // A put on from a field that still shows a doll whose cube was removed elsewhere, or that names another plot, is
    // refused and the row stays the character's.
    [Test]
    public void PutOn_RefusedOnceTheCubeIsRemoved() {
        PlotCube cube = PlaceCube();
        Assert.That(PutOn(cube.Id, PlotId + 1, out long otherPlot), Is.EqualTo(ItemMove.TargetGone));
        Assert.That(Row(otherPlot).OwnerId, Is.EqualTo(Character));

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteCube(cube), Is.True);
        }
        Assert.That(PutOn(cube.Id, PlotId, out long late), Is.EqualTo(ItemMove.TargetGone));
        Maple2.Database.Model.Item row = Row(late);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(Character));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.Default));
        });
    }
}
