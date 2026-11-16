using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Game;

// The restored client's pass bundle and Maple Survival switches (maplesurvival.cpp GameEventMapleSurvivalPassBundle,
// GameEventMapleSurvivalSwitch<0..3>; maplesurvivalbuybundledialog.cpp).
public class MapleSurvivalPassTests {
    private const int PassPrice = 800;
    private const int BonusItemId = 20000995;
    private TestGameSession test = null!;

    // An item id no item table has (a reward item the played pack lacks).
    private const int MissingItemId = 29999999;

    // freeLevel1: the free pass rewards of level 1 (survivalRewardID 1).
    private static MapleSurvivalTable Table(IReadOnlyList<MapleSurvivalTable.Reward>? freeLevel1 = null) => new(
        new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 1, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), PassPrice) },
        new Dictionary<int, long> { [1] = 0, [2] = 2400 },
        new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> {
            [1] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [1] = freeLevel1 ?? [] },
        },
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>());

    // The pass mode (SurvivalContents03 on), 5000 merets.
    private GameSession Start(MapleSurvivalPassBundle? bundle = null, bool passShutdown = false, MapleSurvivalTable? table = null) {
        test = new TestGameSession();
        test.SetTable(table ?? Table());
        test.Register(TestGameSession.Metadata(BonusItemId));
        if (bundle != null) {
            test.AddEvent(930001, GameEventType.MapleSurvivalPassBundle, bundle);
        }
        if (passShutdown) {
            test.AddEvent(930002, GameEventType.ShutdownMapleSurvivalPass, new ShutdownMapleSurvivalPass());
        }
        GameSession session = test.Start();
        session.Currency.Meret = 5000;
        test.TakeSent();
        return session;
    }

    private static int Amount(GameSession session, int itemId) {
        int amount = 0;
        foreach (Item item in session.Item.Inventory.Filter(item => item.Id == itemId)) {
            amount += item.Amount;
        }
        return amount;
    }

    // An ingested row needs both prices and the bonus item: the client's buy-bundle window closes on item 0.
    [TestCase("700", "1500", "20000995", true)]
    [TestCase("700", "1500", "", false)]
    [TestCase("700", "1500", "0", false)]
    [TestCase("", "1500", "20000995", false)]
    [TestCase("700", "0", "20000995", false)]
    public void Ingest_PassBundleRow(string value1, string value2, string value3, bool kept) {
        var mapper = (Maple2.File.Ingest.Mapper.ServerTableMapper) System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(Maple2.File.Ingest.Mapper.ServerTableMapper));
        System.Reflection.MethodInfo parse = typeof(Maple2.File.Ingest.Mapper.ServerTableMapper).GetMethod("ParseGameEventData",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        object? data = parse.Invoke(mapper, [GameEventType.MapleSurvivalPassBundle, value1, value2, value3, ""]);

        Assert.That(data, Is.EqualTo(kept ? new MapleSurvivalPassBundle(700, 1500, 20000995) : null));
    }

    // The 0x02 match box: character 0 (the first-match ranking line) until the character has a game in the season.
    [Test]
    public void RankingCharacter_ZeroUntilTheFirstSeasonGame() {
        GameSession session = Start();
        Assert.That(MapleSurvivalProgress.RankingCharacter(session), Is.EqualTo(0));
        using (Maple2.Database.Storage.GameStorage.Request db = session.GameStorage.Context()) {
            Assert.That(db.SaveSurvivalRecord(session.CharacterId, new Maple2.Model.Game.Pvp.SurvivalSeasonRecord { SeasonId = 1, Rating = 1000, Games = 1 }), Is.True);
        }
        Assert.That(MapleSurvivalProgress.RankingCharacter(session), Is.EqualTo(session.CharacterId));
    }

    // Without the event only the pass window's (true, passPrice); with it also the buy-bundle window's (false, value1)
    // and (true, value2), the latter with the bonus item.
    [Test]
    public void PassPurchase_Prices() {
        var bundle = new MapleSurvivalPassBundle(700, 1500, BonusItemId);
        Assert.Multiple(() => {
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, null, true, PassPrice), Is.EqualTo(((long) PassPrice, 0)));
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, null, false, PassPrice), Is.Null);
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, null, true, 700), Is.Null);
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, bundle, false, 700), Is.EqualTo((700L, 0)));
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, bundle, true, 1500), Is.EqualTo((1500L, BonusItemId)));
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, bundle, true, PassPrice), Is.EqualTo(((long) PassPrice, 0)));
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, bundle, false, 1500), Is.Null);
            Assert.That(MapleSurvivalProgress.PassPurchase(PassPrice, bundle, true, 700), Is.Null);
        });
    }

    // The bundle buy takes value2 merets, activates the pass and gives the bonus item (1).
    // A pass reward whose item cannot be made (KR has no item for most of the NA pass items) is logged and left out;
    // the level's other rewards still go and the level is claimed.
    [Test]
    public void ClaimPass_MissingItem_GivesTheRest() {
        GameSession session = Start(table: Table([
            new MapleSurvivalTable.Reward("item", [MissingItemId], [1], [1]),
            new MapleSurvivalTable.Reward("item", [BonusItemId], [1], [2]),
        ]));
        session.Player.Value.Account.SurvivalLevel = 1;
        Assert.DoesNotThrow(() => MapleSurvivalProgress.ClaimPass(session, 1, 0));
        Assert.That(session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Is.EqualTo(1));
        Assert.That(Amount(session, BonusItemId), Is.EqualTo(2));
        Assert.That(Amount(session, MissingItemId), Is.EqualTo(0));
    }

    private Maple2.Database.Model.Account StoredAccount() {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Account.AsNoTracking().Single(account => account.Id == test.AccountId);
    }

    private int StoredItems(int itemId) {
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        return context.Item.Count(item => item.ItemId == itemId);
    }

    // The levels claimed are stored with their items in one transaction. Before, the items were stored at once and the
    // levels claimed stayed in memory until the session save, so a crash in between let the levels be claimed again.
    [Test]
    public void ClaimPass_StoresTheLevelsWithTheItems() {
        GameSession session = Start(table: Table([new MapleSurvivalTable.Reward("item", [BonusItemId], [1], [2])]));
        session.Player.Value.Account.SurvivalLevel = 1;
        MapleSurvivalProgress.ClaimPass(session, 1, 0);
        Assert.That((StoredAccount().SurvivalSilverLevelRewardClaimed, StoredItems(BonusItemId)), Is.EqualTo((1, 1)));
        Assert.That((session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Amount(session, BonusItemId)), Is.EqualTo((1, 2)));
    }

    // survivalPassReward(_paid).xml hold several reward sets (row id), each with its own levels: a claim gives the
    // level's rewards of the open period's survivalRewardID only, not another set's row for the same level.
    [Test]
    public void ClaimPass_GivesTheSetOfTheOpenPeriod() {
        const int otherItemId = 20000996;
        IReadOnlyList<MapleSurvivalTable.Reward> Level(int itemId, int count) => [new MapleSurvivalTable.Reward("item", [itemId], [1], [count])];
        var table = new MapleSurvivalTable(
            new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 2, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), PassPrice) },
            new Dictionary<int, long> { [1] = 0, [2] = 2400 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> {
                [1] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [1] = Level(otherItemId, 1) },
                [2] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [1] = Level(BonusItemId, 2) },
                [3] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> { [1] = Level(otherItemId, 3) },
            },
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>());
        GameSession session = Start(table: table);
        test.Register(TestGameSession.Metadata(otherItemId));
        session.Player.Value.Account.SurvivalLevel = 1;
        MapleSurvivalProgress.ClaimPass(session, 1, 0);
        Assert.That((Amount(session, BonusItemId), Amount(session, otherItemId)), Is.EqualTo((2, 0)));
    }

    // When the levels claimed cannot be stored, no item is given and nothing is claimed; the claim gives them later.
    [Test]
    public void ClaimPass_LevelsNotStored_NothingGiven() {
        GameSession session = Start(table: Table([new MapleSurvivalTable.Reward("item", [BonusItemId], [1], [2])]));
        session.Player.Value.Account.SurvivalLevel = 1;
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Account>().Any(entry => entry.State == EntityState.Modified);
        MapleSurvivalProgress.ClaimPass(session, 1, 0);
        test.FailSave = null;
        Assert.That((session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Amount(session, BonusItemId)), Is.EqualTo((0, 0)));
        Assert.That((StoredAccount().SurvivalSilverLevelRewardClaimed, StoredItems(BonusItemId)), Is.EqualTo((0, 0)));

        MapleSurvivalProgress.ClaimPass(session, 1, 0);
        Assert.That((session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Amount(session, BonusItemId)), Is.EqualTo((1, 2)));
    }

    // Between fields (Field null) the claim goes and its items are mailed. Before, the claim was refused.
    [Test]
    public void ClaimPass_NoField_MailsTheItems() {
        GameSession session = Start(table: Table([new MapleSurvivalTable.Reward("item", [BonusItemId], [1], [2])]));
        session.Player.Value.Account.SurvivalLevel = 1;
        session.Field = null;
        MapleSurvivalProgress.ClaimPass(session, 1, 0);
        Assert.That((session.Player.Value.Account.SurvivalSilverLevelRewardClaimed, Amount(session, BonusItemId)), Is.EqualTo((1, 0)));
        Assert.That(StoredAccount().SurvivalSilverLevelRewardClaimed, Is.EqualTo(1));
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        long mailId = context.Mail.Where(mail => mail.ReceiverId == session.CharacterId).Select(mail => mail.Id).Single();
        Assert.That(context.Item.Count(item => item.OwnerId == mailId && item.ItemId == BonusItemId), Is.EqualTo(1));
    }

    // A bundle whose bonus item cannot be made is refused before the merets are taken: nothing is bought.
    [Test]
    public void BuyPass_Bundle_MissingBonusItem_Refused() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, MissingItemId));
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        Assert.That((session.Player.Value.Account.ActiveGoldPass, session.Currency.Meret), Is.EqualTo((false, 5000L)));
    }

    [Test]
    public void BuyPass_Bundle_GivesTheBonusItem() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(3500));
        Assert.That(Amount(session, BonusItemId), Is.EqualTo(1));
    }

    // The pass and the merets taken are in the database before the bonus item is a row: a crash after the bonus item
    // finds the pass bought and paid. Before, both waited for the session save while the bonus row was written at
    // once, so a crash kept the bonus and gave the merets back with no pass.
    [Test]
    public void BuyPass_Bundle_StoresThePassAndTheMeretsWithTheBonus() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        MapleSurvivalProgress.BuyPass(session, true, 1500);

        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        Maple2.Database.Model.Account stored = context.Account.Find(session.AccountId)!;
        Assert.Multiple(() => {
            Assert.That(context.Item.Count(item => item.OwnerId == session.CharacterId && item.ItemId == BonusItemId), Is.EqualTo(1));
            Assert.That((stored.ActiveGoldPass, stored.Currency.Meret), Is.EqualTo((true, 3500L)));
        });
    }

    // A pass whose store is refused is not bought: the merets stay, no pass and no bonus item.
    [Test]
    public void BuyPass_StoreRefused_NothingBought() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        test.FailSave = context => context.ChangeTracker.Entries<Maple2.Database.Model.Account>()
            .Any(entry => entry.State == Microsoft.EntityFrameworkCore.EntityState.Modified);
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        test.FailSave = null;

        Assert.Multiple(() => {
            Assert.That((session.Player.Value.Account.ActiveGoldPass, session.Currency.Meret), Is.EqualTo((false, 5000L)));
            Assert.That(Amount(session, BonusItemId), Is.EqualTo(0));
        });
    }

    // Between fields (Field null) the bonus item is made all the same and goes by mail.
    [Test]
    public void BuyPass_Bundle_NoField_MailsTheBonusItem() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        session.Field = null;
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.True);
        Assert.That(Amount(session, BonusItemId), Is.EqualTo(0));
        using var context = new Maple2.Database.Context.Ms2Context(test.Options);
        long mailId = context.Mail.Where(mail => mail.ReceiverId == session.CharacterId).Select(mail => mail.Id).Single();
        Assert.That(context.Item.Count(item => item.OwnerId == mailId && item.ItemId == BonusItemId), Is.EqualTo(1));
    }

    [Test]
    public void BuyPass_BundleDefault_NoBonusItem() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        MapleSurvivalProgress.BuyPass(session, false, 700);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(4300));
        Assert.That(Amount(session, BonusItemId), Is.EqualTo(0));
    }

    // Without the event the bundle prices are refused; the season's price buys.
    [Test]
    public void BuyPass_NoBundle_OnlyThePassPrice() {
        GameSession session = Start();
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.False);
        MapleSurvivalProgress.BuyPass(session, true, PassPrice);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000 - PassPrice));
    }

    // A ShutdownMapleSurvivalPass event closes the buy: nothing taken.
    [Test]
    public void BuyPass_PassShutdown_Refused() {
        GameSession session = Start(passShutdown: true);
        MapleSurvivalProgress.BuyPass(session, true, PassPrice);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.False);
        Assert.That(session.Currency.Meret, Is.EqualTo(5000));
    }

    private static GameEvent Event(int id, GameEventType type, GameEventData data) =>
        new(new GameEventMetadata(id, type, DateTime.MinValue, DateTime.MaxValue, TimeSpan.Zero, TimeSpan.Zero, [], data, "", "", "", ""));

    // GameEventMapleSurvivalPassBundle::vfunc_4: id, i64, i64, i32; the switches: the id (GameEvent::vfunc_4). Only the
    // restored client gets them; the KR client has no class and would drop the rest of the list.
    [Test]
    public void Packets_RestoredClientOnly() {
        GameEvent bundle = Event(930001, GameEventType.MapleSurvivalPassBundle, new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        GameEvent[] switches = [
            Event(910100, GameEventType.ShutdownMapleSurvival, new ShutdownMapleSurvival()),
            Event(930003, GameEventType.ShutdownMapleSurvivalSquad, new ShutdownMapleSurvivalSquad()),
            Event(930002, GameEventType.ShutdownMapleSurvivalPass, new ShutdownMapleSurvivalPass()),
            Event(930004, GameEventType.ShutdownSurvivalObserver, new ShutdownSurvivalObserver()),
        ];
        string? before = Environment.GetEnvironmentVariable("MS2_SURVIVAL_CLIENT");
        try {
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", "0");
            Assert.That(bundle.SentToClient, Is.False);
            foreach (GameEvent gameEvent in switches) {
                Assert.That(gameEvent.SentToClient, Is.False);
            }
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", "1");
            Assert.That(bundle.SentToClient, Is.True);
            var writer = new ByteWriter();
            bundle.WriteTo(writer);
            var reader = new ByteReader(writer.Buffer, 0);
            Assert.That(reader.ReadUnicodeString(), Is.EqualTo("MapleSurvivalPassBundle"));
            Assert.That((reader.ReadInt(), reader.ReadLong(), reader.ReadLong(), reader.ReadInt()), Is.EqualTo((930001, 700L, 1500L, BonusItemId)));
            Assert.That(reader.Position, Is.EqualTo(writer.Length));
            foreach (GameEvent gameEvent in switches) {
                Assert.That(gameEvent.SentToClient, Is.True);
                writer = new ByteWriter();
                gameEvent.WriteTo(writer);
                reader = new ByteReader(writer.Buffer, 0);
                Assert.That(reader.ReadUnicodeString(), Is.EqualTo(gameEvent.Metadata.Type.ToString()));
                Assert.That(reader.ReadInt(), Is.EqualTo(gameEvent.Id));
                Assert.That(reader.Position, Is.EqualTo(writer.Length));
            }
        } finally {
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", before);
        }
    }

    // KR paid pass set 1 (survivalPassReward_paid.xml, NA rows): level 1 is the additionalEffect 70000187 at level 1,
    // level 3 the genderItem 11500248 (male) / 11500249 (female). A paid claim to level 3 with the pass gives the
    // female character the second item and starts the buff after the store; a reward type the server does not give
    // (here "title") is logged and the level is still claimed.
    [Test]
    public void ClaimPass_Paid_BuffAndGenderItem() {
        const int buffId = 70000187;
        var table = new MapleSurvivalTable(
            new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 1, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), PassPrice) },
            new Dictionary<int, long> { [1] = 0, [2] = 2400, [3] = 4320 },
            new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
            new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>> {
                [1] = new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>> {
                    [1] = [new MapleSurvivalTable.Reward("additionalEffect", [buffId], [], [1])],
                    [2] = [new MapleSurvivalTable.Reward("title", [1], [], [1])],
                    [3] = [new MapleSurvivalTable.Reward("genderItem", [11500248, 11500249], [1, 1], [1, 1])],
                },
            });
        test = new TestGameSession(gender: Gender.Female);
        test.SetTable(table);
        test.Register(TestGameSession.Metadata(11500248), TestGameSession.Metadata(11500249));
        GameSession session = test.Start();
        test.EnterMap(TestGameSession.Map(2000001));
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var skills = new Maple2.Database.Storage.SkillMetadataStorage(new Maple2.Database.Context.MetadataContext(
            new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options));
        object cache = typeof(Maple2.Database.Storage.SkillMetadataStorage).GetField("EffectCache", any)!.GetValue(skills)!;
        AdditionalEffectMetadata effect = BuffAttributeTests.Effect(buffId, DisabledBuffTests.Property with { DurationTick = 10000 });
        cache.GetType().GetMethod("AddReplace")!.Invoke(cache, [(effect.Id, effect.Level), effect]);
        var field = session.Field!;
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField("<SkillMetadata>k__BackingField", any)!.SetValue(field, skills);
        typeof(Maple2.Server.Game.Manager.Field.FieldManager).GetField(nameof(field.FieldActor), any)!
            .SetValue(field, new Maple2.Server.Game.Model.FieldActor(field, 99, field.Metadata, null!));
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<Field>k__BackingField", any)!.SetValue(session.Player, field);
        session.Player.Value.Account.SurvivalLevel = 3;
        session.Player.Value.Account.ActiveGoldPass = true;
        test.TakeSent();

        MapleSurvivalProgress.ClaimPass(session, 0, 3);

        Assert.Multiple(() => {
            Assert.That(session.Player.Value.Account.SurvivalGoldLevelRewardClaimed, Is.EqualTo(3));
            Assert.That(Amount(session, 11500249), Is.EqualTo(1));
            Assert.That(Amount(session, 11500248), Is.EqualTo(0));
            Assert.That(session.Player.Buffs.EnumerateBuffs(buffId).Count(), Is.EqualTo(1));
            Assert.That(test.Errors, Is.Empty);
        });
    }
}
