using System;
using System.Linq;
using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// A new black market listing carries its creation time: the client's own listings window shows it as the listing's
// create time (UIBlackMarketPage, uiblackmarketpage.cpp:1717, CBlackMarketProduct m_qw20). It was never set, so the
// listing went out and was stored as 0 (1970).
public class BlackMarketCreationTimeTests {
    [Test]
    public void Add_StoresAndSendsTheTimeOfTheListing() {
        var test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(20000005), amount: 5);
        test.Start();
        test.Session.Currency.Meso = 1_000_000;
        test.TakeSent();
        long before = DateTime.Now.ToEpochSeconds();

        test.Session.BlackMarket.Add(item.Uid, 300, 5);

        long after = DateTime.Now.ToEpochSeconds();
        using GameStorage.Request db = test.Storage.Context();
        BlackMarketListing listing = db.GetBlackMarketListings(test.CharacterId).Single();
        Assert.That(listing.CreationTime, Is.InRange(before, after));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Does.Contain(Convert.ToHexString(BlackMarketPacket.Add(listing).ToArray())));
    }
}
