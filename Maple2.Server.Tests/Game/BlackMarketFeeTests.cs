using Maple2.Model.Game;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game;

// The seller's share of a black market sale. A premium club seller's fee is 20% lower
// (s_vip_dialog_benefit_desc), and the vip mail shows that lowered fee ("[{7} Discounted Sales Fee] {8} mesos").
public class BlackMarketFeeTests {
    private static BlackMarketListing Listing() {
        var item = new Item(TestGameSession.Metadata(20000005), amount: 3);
        return new BlackMarketListing(item) { CharacterId = 1, Price = 1000, Quantity = 2, Deposit = 10 };
    }

    // A 10% fee (a fee event's rate, 1000 / 10000) on one sold at 1000: 100, or 80 for a premium seller.
    [TestCase(false, 900)]
    [TestCase(true, 920)]
    public void Seller_PaysTheFee_LessTheClubDiscount(bool premium, long revenue) {
        Mail mail = BlackMarketManager.SellerMail(Listing(), 1, premium, 1000);

        Assert.That(mail.Meso, Is.EqualTo(revenue));
        if (premium) {
            Assert.That(mail.ContentArgs[^1], Is.EqualTo(("money", "80")));
            Assert.That(mail.ContentArgs[^2], Is.EqualTo(("str", "20%")));
        }
    }
}
