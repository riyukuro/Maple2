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

// Item rows of the game DB whose item the played client lacks (ItemMetadataProperty.NotInPlayedClient) load as
// no item, the way a row without item data does; the row itself stays.
public class PlayedClientItemStorageTests {
    private const int Known = 20000001;
    private const int Marked = 20000002;
    private GameStorage storage = null!;
    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        options = new DbContextOptionsBuilder()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        storage = new GameStorage(options, Storage(Metadata(Known, false), Metadata(Marked, true)), null!, null!, null!, null!, null!,
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

    [Test]
    public void MarkedItem_LoadsAsNoItem_RowStays() {
        using GameStorage.Request db = storage.Context();
        Item? known = db.CreateItem(1, new Item(Metadata(Known, false)));
        // The row is written; the item read back from it is left out.
        Assert.That(db.CreateItem(1, new Item(Metadata(Marked, false))), Is.Null);
        Assert.That(known, Is.Not.Null);

        Assert.Multiple(() => {
            Assert.That(db.GetItem(known!.Uid)?.Id, Is.EqualTo(Known));
            Assert.That(db.GetItemGroups(1, ItemGroup.Default)[ItemGroup.Default].ConvertAll(item => item.Id), Is.EqualTo(new[] { Known }));
            Assert.That(db.GetItemGroupsNoTracking(1, ItemGroup.Default)[ItemGroup.Default], Has.Count.EqualTo(1));
        });

        using var check = new Ms2Context(options);
        Assert.That(check.Item.Count(), Is.EqualTo(2));
    }
}
