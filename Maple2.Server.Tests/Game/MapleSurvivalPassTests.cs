using System;
using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// The restored client's pass bundle and Maple Survival switches (maplesurvival.cpp GameEventMapleSurvivalPassBundle,
// GameEventMapleSurvivalSwitch<0..3>; maplesurvivalbuybundledialog.cpp).
public class MapleSurvivalPassTests {
    private const int PassPrice = 800;
    private const int BonusItemId = 20000995;
    private TestGameSession test = null!;

    private static MapleSurvivalTable Table() => new(
        new Dictionary<int, MapleSurvivalTable.Period> { [1] = new(1, 1, new DateTime(2018, 8, 22), new DateTime(2099, 1, 1), PassPrice) },
        new Dictionary<int, long> { [1] = 0, [2] = 2400 },
        new Dictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>(),
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<MapleSurvivalTable.Reward>>>());

    // The pass mode (SurvivalContents03 on), 5000 merets.
    private GameSession Start(MapleSurvivalPassBundle? bundle = null, bool passShutdown = false) {
        test = new TestGameSession();
        test.SetTable(Table());
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
    [Test]
    public void BuyPass_Bundle_GivesTheBonusItem() {
        GameSession session = Start(new MapleSurvivalPassBundle(700, 1500, BonusItemId));
        MapleSurvivalProgress.BuyPass(session, true, 1500);
        Assert.That(session.Player.Value.Account.ActiveGoldPass, Is.True);
        Assert.That(session.Currency.Meret, Is.EqualTo(3500));
        Assert.That(Amount(session, BonusItemId), Is.EqualTo(1));
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
            Environment.SetEnvironmentVariable("MS2_SURVIVAL_CLIENT", null);
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
}
