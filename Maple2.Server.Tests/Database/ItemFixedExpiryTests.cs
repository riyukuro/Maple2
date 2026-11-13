using System;
using Maple2.Model;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Database;

// Items of a fixed end date (life expirationTimestamp) were made with that past date as their expiry, so they were
// expired from the start (the card game's coins, 30000536). Such a stored row loads with no expiry.
public class ItemFixedExpiryTests {
    private const int Coin = 30000536;
    private const long PastEnd = 1464264000; // 2016-05-26

    private const long Week = 7 * 24 * 3600;

    private static ItemMetadata Metadata(long end, long duration = 0) =>
        TestGameSession.Metadata(Coin) with { Life = new ItemMetadataLife(duration, end) };

    private static Item Load(ItemMetadata metadata, long expiry) {
        var made = new Item(metadata) { ExpiryTime = expiry };
        Maple2.Database.Model.Item row = made!;
        return row.Convert(metadata);
    }

    [Test]
    public void RowOfThePastFixedEnd_LoadsUnexpired() {
        Item loaded = Load(Metadata(PastEnd), PastEnd);

        Assert.That(loaded.ExpiryTime, Is.EqualTo(0));
        Assert.That(loaded.IsExpired(), Is.False);
    }

    [Test]
    public void RowOfAnotherExpiry_KeepsIt() {
        Assert.That(Load(Metadata(PastEnd), PastEnd + 60).ExpiryTime, Is.EqualTo(PastEnd + 60));
        Assert.That(Load(Metadata(0), PastEnd).ExpiryTime, Is.EqualTo(PastEnd));
    }

    [Test]
    public void FutureFixedEnd_IsKept() {
        const long now = 1700000000;
        Assert.That(Item.FixedExpiry(now + 10, now), Is.EqualTo(now + 10));
        Assert.That(Item.FixedExpiry(now - 10, now), Is.EqualTo(0));
        Assert.That(Item.LoadedExpiry(now + 10, new ItemMetadataLife(0, now + 10), now), Is.EqualTo(now + 10));
        Assert.That(Item.LoadedExpiry(now - 10, new ItemMetadataLife(0, now - 10), now), Is.EqualTo(0));
    }

    // An item of a past end date and a lifetime (usePeriod, weeks or months) lasts that lifetime, made or loaded.
    [Test]
    public void PastFixedEndWithALifetime_LastsTheLifetime() {
        const long now = 1700000000;
        Assert.That(Item.MadeExpiry(new ItemMetadataLife(Week, now - 10), now), Is.EqualTo(now + Week));
        Assert.That(Item.MadeExpiry(new ItemMetadataLife(Week, now + 10), now), Is.EqualTo(now + 10));
        Assert.That(Item.MadeExpiry(new ItemMetadataLife(Week, 0), now), Is.EqualTo(now + Week));
        Assert.That(Item.MadeExpiry(new ItemMetadataLife(0, 0), now), Is.EqualTo(0));
        Assert.That(Item.LoadedExpiry(now - 10, new ItemMetadataLife(Week, now - 10), now), Is.EqualTo(now + Week));
        Assert.That(Item.LoadedExpiry(now + 60, new ItemMetadataLife(Week, now - 10), now), Is.EqualTo(now + 60));
    }

#if !DEBUG
    [Test]
    public void NewItemOfAPastFixedEndAndALifetime_LastsTheLifetime() {
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new Item(Metadata(PastEnd, Week));
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(item.ExpiryTime, Is.InRange(before + Week, after + Week));
    }
#endif

    [Test]
    public void RowOfThePastFixedEndWithALifetime_LoadsWithTheLifetime() {
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Item loaded = Load(Metadata(PastEnd, Week), PastEnd);
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(loaded.ExpiryTime, Is.InRange(before + Week, after + Week));
    }
}
