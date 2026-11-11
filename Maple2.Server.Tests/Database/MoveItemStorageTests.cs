using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
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
    private GameStorage storage = null!;
    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, Storage(Metadata(ItemId, false)), null!, null!, null!, null!, null!,
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

    // A put on whose inventory item no longer has the character's row writes nothing; the handler then keeps the
    // item where it was and leaves the doll unchanged.
    [Test]
    public void PutOn_RowNotTheCharacters_WritesNothing() {
        long uid = CreateOnDoll();
        Item copy = LoadFromDoll();
        copy.Slot = (short) EquipSlot.CP;
        using GameStorage.Request db = storage.Context();

        Assert.That(db.MoveItem(copy, Character, ItemGroup.Default, Doll), Is.False);
        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(Doll));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.FittingDoll));
            Assert.That(row.Slot, Is.EqualTo((short) EquipSlot.CL));
        });
    }

    [Test]
    public void PutOn_MovesTheCharactersRowToTheDoll() {
        Item item = Create();
        long uid = item.Uid;
        item.Group = ItemGroup.FittingDoll;
        item.Slot = (short) EquipSlot.CL;
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.MoveItem(item, Character, ItemGroup.Default, Doll), Is.True);
        }
        Maple2.Database.Model.Item row = Row(uid);
        Assert.Multiple(() => {
            Assert.That(row.OwnerId, Is.EqualTo(Doll));
            Assert.That(row.Group, Is.EqualTo(ItemGroup.FittingDoll));
        });
    }
}
