using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.Manager;

// The KR currency rows (item 9000xxxx, type 4): a picked up or mailed one is used up into the wallet it names, never
// kept in the bag.
public class InventoryCurrencyItemTests {
    private static GameSession Started(out TestGameSession test) {
        test = new TestGameSession();
        // The KR caps (constants.xml): HonorTokenMax 5000, KarmaTokenMax 75000, LuTokenMax 2000, HabiTokenMax 35000,
        // ReverseCoinMax 2500 (KR), MenteeTokenMax 35000.
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with {
            HonorTokenMax = 5000, KarmaTokenMax = 75000, LuTokenMax = 2000, HabiTokenMax = 35000, ReverseCoinMax = 2500, MenteeTokenMax = 35000,
        });
        GameSession session = test.Start();
        test.TakeSent();
        return session;
    }

    private static Maple2.Model.Game.Item Currency(int id, int amount) => new(TestGameSession.Metadata(id, type: 4), amount: amount);

    // 90000013 Rue, 90000014 Havi Fruit, 90000017 Treva, 90000022 Reverse Coin, 90000024 Mentee Point,
    // 90000025 Star Point.
    [TestCase(90000013, CurrencyType.Rue)]
    [TestCase(90000014, CurrencyType.HaviFruit)]
    [TestCase(90000017, CurrencyType.Treva)]
    [TestCase(90000022, CurrencyType.ReverseCoin)]
    [TestCase(90000024, CurrencyType.MenteeToken)]
    [TestCase(90000025, CurrencyType.StarPoint)]
    public void Row_GoesToItsWallet(int id, CurrencyType type) {
        GameSession session = Started(out TestGameSession test);

        Assert.That(session.Item.Inventory.Add(Currency(id, 7)), Is.True);

        Assert.That(session.Currency[type], Is.EqualTo(7));
        Assert.That(session.Item.Inventory.Find(id), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }

    // 90000009 Spirit Orb and 90000010 Stamina Orb fill the bar they name, up to its total.
    [TestCase(90000009, BasicAttribute.Spirit)]
    [TestCase(90000010, BasicAttribute.Stamina)]
    public void Orb_FillsItsBar(int id, BasicAttribute bar) {
        GameSession session = Started(out TestGameSession test);
        var stat = session.Player.Stats.Values[bar];
        stat.Total = 100;
        stat.Current = 10;

        Assert.That(session.Item.Inventory.Add(Currency(id, 30)), Is.True);
        Assert.That(stat.Current, Is.EqualTo(40));
        Assert.That(session.Item.Inventory.Add(Currency(id, 500)), Is.True);
        Assert.That(stat.Current, Is.EqualTo(100));
        Assert.That(test.TakeSentOps(), Has.Some.EqualTo(Maple2.Server.Core.Constants.SendOp.Stat));
    }

    // 90000005 Dungeon Key has no wallet: it is used up and gives nothing, and the bag stays empty.
    [Test]
    public void DungeonKey_GivesNothing() {
        GameSession session = Started(out TestGameSession test);

        Assert.That(session.Item.Inventory.Add(Currency(90000005, 3)), Is.True);

        Assert.That(session.Item.Inventory.Find(90000005), Is.Empty);
        Assert.That(test.Errors, Is.Empty);
    }
}
