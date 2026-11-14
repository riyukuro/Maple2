using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// A meret market search answers five pages from the asked start page. The client always asks 0x12 items a page
// (meratmarketbasketinfo.cpp:1793); the page byte was taken as it came (255 gave 1275 entries) and a large start
// page wrapped the int offset to an earlier page.
public class MeretMarketPageTests {
    private sealed class Entry(int index) : MarketItem(TestGameSession.Metadata(20000001)) {
        public readonly int Index = index;
    }

    private static int[] Take(int startPage, int perPage) =>
        MeretMarketHandler.TakeLimit(Enumerable.Range(0, 1000).Select(i => new Entry(i)), startPage, perPage)
            .Cast<Entry>().Select(entry => entry.Index).ToArray();

    [Test]
    public void TheClientsPage_IsFivePagesFromTheStart() {
        Assert.Multiple(() => {
            Assert.That(Take(1, 0x12), Is.EqualTo(Enumerable.Range(0, 90).ToArray()));
            Assert.That(Take(6, 0x12), Is.EqualTo(Enumerable.Range(90, 90).ToArray()));
        });
    }

    [Test]
    public void ALargerPageByte_IsTheClientsPageSize() {
        Assert.That(Take(1, 255), Is.EqualTo(Enumerable.Range(0, 5 * MeretMarketHandler.MaxItemsPerPage).ToArray()));
    }

    [Test]
    public void AHugeStartPage_GivesNothing() {
        Assert.That(Take(int.MaxValue / 2, 0x12), Is.Empty);
    }

    [Test]
    public void AStartPageBelow1_StartsAtTheFirstEntry() {
        Assert.That(Take(0, 0x12), Is.EqualTo(Enumerable.Range(0, 72).ToArray()));
    }
}
