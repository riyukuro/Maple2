using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Database;

// A cube placed before FixedExpiry holds its item's past fixed end date in its row. It loads with no expiry, so a
// pickup gives back the expiry a new item is made with (KeptExpiry null), not an expired item. Other expiries stay.
public class CubeFixedExpiryTests {
    private const int CubeItemId = 50400001;
    private const long PlotId = 7001;
    private const long PastEnd = 1464264000; // 2016-05-26
    private const long Week = 7 * 24 * 3600;

    private static ItemMetadata Metadata(long duration) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(CubeItemId, null, [], "", [], new ItemMetadataLife(duration, PastEnd), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, new ItemMetadataHousing(0, 0, 0, default, true, 3), null);
    }

    private static GameStorage Storage(ItemMetadata metadata) {
        var itemStorage = (ItemMetadataStorage) RuntimeHelpers.GetUninitializedObject(typeof(ItemMetadataStorage));
        var cache = new LRUCache<int, ItemMetadata>(16, 1);
        cache.AddReplace(metadata.Id, metadata);
        typeof(MetadataStorage<int, ItemMetadata>).GetField("Cache", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(itemStorage, cache);
        DbContextOptions options = new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new GameStorage(options, itemStorage, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    private static PlotCube Reload(ItemMetadata metadata, long expiry) {
        GameStorage storage = Storage(metadata);
        var plot = (PlotInfo) RuntimeHelpers.GetUninitializedObject(typeof(PlotInfo));
        plot.Id = PlotId;
        var cube = new PlotCube(metadata) { Position = new Vector3B(1, 2, 3), Type = PlotCube.CubeType.Construction, ExpiryTime = expiry };
        long id;
        using (GameStorage.Request db = storage.Context()) {
            id = db.CreateCube(plot, cube)!.Id;
        }
        using GameStorage.Request read = storage.Context();
        return read.GetCube(id)!;
    }

    [TestCase(0)]
    [TestCase(Week)]
    public void CubeOfThePastFixedEnd_GoesBackAsANewItem(long duration) {
        ItemMetadata metadata = Metadata(duration);
        PlotCube loaded = Reload(metadata, PastEnd);

        Assert.That(loaded.ExpiryTime, Is.EqualTo(0));
        Assert.That(FurnishingManager.KeptExpiry(metadata, loaded.ExpiryTime), Is.Null);
    }

    [Test]
    public void CubeOfAnotherExpiry_KeepsIt() {
        ItemMetadata metadata = Metadata(Week);
        PlotCube loaded = Reload(metadata, PastEnd + 60);

        Assert.That(loaded.ExpiryTime, Is.EqualTo(PastEnd + 60));
        Assert.That(FurnishingManager.KeptExpiry(metadata, loaded.ExpiryTime), Is.EqualTo(PastEnd + 60));
    }
}
