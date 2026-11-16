using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Config;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// SkillManager.ExpandSkillTabs: one more tab in the skill book, paid with a skill tab coupon when asked for and owned,
// otherwise with the price in merets (SkillBookTreeAddTabFeeMerat; uiskillbookautodistributedialog.cpp:1091-1115). Free
// while a tab is open, refused at the cap (Constant.MaxSkillTabCount).
public class SkillTabExpandTests {
    private const int Price = 1000;
    private TestGameSession test = null!;
    private GameSession session = null!;

    private void Start(int tabs, int maxTabs, ItemMetadata? coupon = null) {
        test = new TestGameSession();
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { SkillBookTreeAddTabFeeMerat = Price });
        if (coupon != null) {
            test.Store(coupon);
        }
        test.Start();
        session = test.Session;
        test.EnterMap(TestGameSession.Map(2000001));
        var book = new SkillBook { MaxSkillTabs = maxTabs };
        for (int i = 0; i < tabs; i++) {
            book.SkillTabs.Add(new SkillTab($"tab{i}") { Id = i + 1 });
        }
        test.SetTable(new JobTable(new System.Collections.Generic.Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new System.Collections.Generic.Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        typeof(ConfigManager).GetField(nameof(ConfigManager.Skill))!.SetValue(session.Config, new SkillManager(session, book));
        session.Currency.Meret = 5000;
        test.TakeSent();
    }

    private int Max => session.Config.Skill.SkillBook.MaxSkillTabs;

    [Test]
    public void AnOpenTabCostsNothing() {
        Start(tabs: 1, maxTabs: 2);

        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.ExpandSkillTabs(), Is.True);
            Assert.That(Max, Is.EqualTo(2));
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void TheMeretPriceIsTakenAndATabAdded() {
        Start(tabs: 1, maxTabs: 1);

        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.ExpandSkillTabs(), Is.True);
            Assert.That(Max, Is.EqualTo(2));
            Assert.That(session.Currency.Meret, Is.EqualTo(5000 - Price));
            Assert.That(test.TakeSentOps(), Does.Contain(SendOp.SkillBookTree));
        });
    }

    [Test]
    public void WithoutTheMeretsOrAtTheCapNothingIsTaken() {
        Start(tabs: 1, maxTabs: 1);
        session.Currency.Meret = Price - 1;
        test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.ExpandSkillTabs(), Is.False);
            Assert.That(Max, Is.EqualTo(1));
            Assert.That(session.Currency.Meret, Is.EqualTo(Price - 1));
        });

        Start(tabs: Constant.MaxSkillTabCount, maxTabs: Constant.MaxSkillTabCount);
        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.ExpandSkillTabs(), Is.False);
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
        });
    }

    // A coupon is used only when asked for and owned: it is taken instead of the merets.
    [Test]
    public void ACouponPaysWhenAskedForAndOwned() {
        ItemMetadata metadata = TestGameSession.Metadata(20000077);
        ItemMetadata coupon = metadata with { Property = metadata.Property with { Tag = ItemTag.SkillBookTreeAddTabCoupon } };
        Start(tabs: 1, maxTabs: 1, coupon);

        Assert.Multiple(() => {
            Assert.That(session.Config.Skill.ExpandSkillTabs(useCoupon: true), Is.True);
            Assert.That(Max, Is.EqualTo(2));
            Assert.That(session.Currency.Meret, Is.EqualTo(5000));
            Assert.That(session.Item.Inventory.Find(ItemTag.SkillBookTreeAddTabCoupon), Is.Empty);
        });
    }
}
