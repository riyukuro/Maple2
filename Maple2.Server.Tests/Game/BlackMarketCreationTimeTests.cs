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

    // The listing and the deposit are stored in one save. What a stop at the store's account lock would leave (the
    // database read there) holds no listing without its deposit: before, the listing was committed on its own and the
    // deposit only after, at that lock, so a stop there kept the listing with the deposit not taken.
    [Test]
    public void Add_NoListingIsStoredWithoutItsDeposit() {
        var test = new TestGameSession();
        Item item = test.Store(TestGameSession.Metadata(20000005), amount: 5);
        test.Start();
        test.Session.Currency.Meso = 1_000_000;
        Assert.That(test.Session.SavePurchase(), Is.True);
        long characterId = test.CharacterId;
        (bool Listed, long Meso) Stored() {
            using var context = new Maple2.Database.Context.Ms2Context(test.Options);
            return (context.BlackMarketListing.Any(listing => listing.CharacterId == characterId),
                context.Character.Single(character => character.Id == characterId).Currency.Meso);
        }
        var atLock = new System.Collections.Generic.List<(bool Listed, long Meso)>();
        test.World.OnAcquire = () => atLock.Add(Stored());

        test.Session.BlackMarket.Add(item.Uid, 300, 5);
        test.World.OnAcquire = null;

        long paid = 1_000_000 - test.Session.Currency.Meso;
        Assert.Multiple(() => {
            Assert.That(paid, Is.GreaterThan(0));
            Assert.That(atLock, Has.None.Matches<(bool Listed, long Meso)>(state => state.Listed && state.Meso == 1_000_000));
            Assert.That(Stored(), Is.EqualTo((true, 1_000_000 - paid)));
        });
    }
}
