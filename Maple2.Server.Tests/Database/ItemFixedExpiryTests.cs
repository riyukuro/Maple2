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

    private static ItemMetadata Metadata(long end) => TestGameSession.Metadata(Coin) with { Life = new ItemMetadataLife(0, end) };

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
        Assert.That(Item.LoadedExpiry(now + 10, now + 10, now), Is.EqualTo(now + 10));
        Assert.That(Item.LoadedExpiry(now - 10, now - 10, now), Is.EqualTo(0));
    }
}
