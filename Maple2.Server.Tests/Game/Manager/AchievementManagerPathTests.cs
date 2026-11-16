using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Session;
using StoredItem = Maple2.Model.Game.Item;

namespace Maple2.Server.Tests.Game.Manager;

// AchievementManager paths the other trophy tests leave: the decoration score counter, the load of the character's
// own trophies at login, and a claimed item with a full bag. The trophy and item ids are made up.
public class AchievementManagerPathTests {
    private const int Trophy = 90000501;
    private const int RewardItem = 20000001;

    private TestGameSession test = null!;

    [SetUp]
    public void SetUp() {
        test = new TestGameSession();
    }

    private GameSession Start(params AchievementMetadata[] trophies) {
        test.AddAchievement(trophies);
        test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        test.TakeSent();
        return test.Session;
    }

    // The home's decoration score (interior_point) is a score, not a sum: a higher score moves the counter to it,
    // a lower one (furniture taken away) sends nothing and keeps it.
    [Test]
    public void InteriorPoint_KeepsTheHighestScore() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.interior_point, [300, 1100]));

        session.ConditionUpdate(ConditionType.interior_point, counter: 200);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(200));
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Achieve }));

        session.ConditionUpdate(ConditionType.interior_point, counter: 150);
        session.ConditionUpdate(ConditionType.interior_point, counter: 200);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(200));
        Assert.That(test.TakeSentOps(), Is.Empty);

        session.ConditionUpdate(ConditionType.interior_point, counter: 350);
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(350));
        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Achieve }));
        Assert.That(test.Errors, Is.Empty);
    }

    // The login load: the trophy window's reset, then the account's trophies and the character's own.
    [Test]
    public void Load_SendsTheCharactersTrophies() {
        GameSession session = Start(TestGameSession.Trophy(Trophy, ConditionType.use_merat, [10, 1000]));
        session.Currency.Meret = 100;
        session.Currency.Meret -= 20;
        test.TakeSent();

        session.Achievement.Load();

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.Achieve, SendOp.Achieve }));
        Assert.That(test.Errors, Is.Empty);
    }

    // A claimed item grade with no room in the bag: the grade is claimed and the item comes by mail.
    [Test]
    public void Claim_AnItemWithAFullBag_IsMailed() {
        test.Register(TestGameSession.Metadata(RewardItem));
        AchievementMetadata trophy = TestGameSession.Trophy(Trophy, ConditionType.use_merat, [10, 1000]);
        var grades = new Dictionary<int, AchievementMetadataGrade>(trophy.Grades);
        grades[1] = grades[1] with { Reward = new AchievementMetadataReward(AchievementRewardType.item, RewardItem, 3, 1) };
        GameSession session = Start(trophy with { Grades = grades });
        // Items are owned by a character, account or mail id; the in-memory database starts each at 1, so one mail id
        // is used up first (see BagSplitTests.Mail).
        using (GameStorage.Request db = test.Storage.Context()) {
            db.CreateMail(new Mail { ReceiverId = test.CharacterId + 1, Type = MailType.System, Title = "t", Content = "c" });
        }
        LoadConfig(session);
        StoredItem sample = session.Field!.ItemDrop.CreateItem(RewardItem)!;
        BagSplitTests.Bag(session, sample, (short) (session.Item.Inventory.Filter(_ => true, sample.Inventory).Count + 1), locked: 1);
        session.Currency.Meret = 100;
        session.Currency.Meret -= 10;
        test.TakeSent();

        session.Achievement.ClaimReward(Trophy);

        using GameStorage.Request check = test.Storage.Context();
        Assert.Multiple(() => {
            Assert.That(check.GetAllMail(test.CharacterId).SelectMany(mail => mail.Items).Where(item => item.Id == RewardItem)
                .Sum(item => item.Amount), Is.EqualTo(3));
            Assert.That(check.GetAchievements(test.CharacterId)[Trophy].RewardGrade, Is.EqualTo(2));
            Assert.That(test.Errors, Is.Empty);
        });
    }

    // The config a logged-in session holds (an item grade's claim stores it with the claimed grade).
    private void LoadConfig(GameSession session) {
        test.SetTable(new JobTable(new Dictionary<JobCode, JobTable.Entry> {
            [JobCode.Newbie] = new(new JobTable.Tutorial(0, 0, 0, [], [], [], []), new Dictionary<SkillRank, JobTable.Skill[]> {
                [SkillRank.Basic] = [], [SkillRank.Awakening] = [],
            }, []),
        }));
        test.SetTable(new PrestigeLevelRewardTable(new Dictionary<int, PrestigeLevelRewardMetadata>()));
        using GameStorage.Request db = test.Storage.Context();
        session.Config = new Maple2.Server.Game.Manager.ConfigManager(db, session);
        test.TakeSent();
    }
}
