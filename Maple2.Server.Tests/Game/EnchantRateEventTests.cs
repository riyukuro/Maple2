using System;
using System.Collections.Generic;
using Maple2.File.Ingest.Helpers;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// GameEventEnchantRate: value1 the enchant level a try reaches, value2 the raise in percent. The client keeps the
// highest raise per level and its Ophelia dialog shows the success rate raised by it, up to 100, with the fodder and
// charge rates on top (FormatEnchantRateText). The server's Ophelia roll uses the same total. The event ids are made up.
public class EnchantRateEventTests {
    [TestCase("14", "900", 14, 900)]
    [TestCase(" 3 ", " 20 ", 3, 20)]
    public void Reader_LevelAndRaise(string value1, string value2, int level, int rate) {
        Assert.That(EventSaleReader.EnchantRate(value1, value2), Is.EqualTo(new EnchantRate(level, rate)));
    }

    [TestCase("0", "10")]
    [TestCase("", "10")]
    [TestCase("x", "10")]
    [TestCase("14", "0")]
    [TestCase("14", "-5")]
    [TestCase("14", "")]
    [TestCase(null, null)]
    public void Reader_NoLevelOrRaiseGivesNoEvent(string? value1, string? value2) {
        Assert.That(EventSaleReader.EnchantRate(value1, value2), Is.Null);
    }

    // base * (1 + 0.01f * raise) as a float, up to 100; nothing without a raise.
    [TestCase(10, 900, 100f)]
    [TestCase(10, 50, 15f)]
    [TestCase(30, 0, 30f)]
    [TestCase(60, 100, 100f)]
    [TestCase(15, 33, 19.95f)]
    public void SuccessWithEvent_IsTheClientRule(int success, int raise, float expected) {
        Assert.That(EventSaleRules.EnchantSuccessWithEvent(success, raise), Is.EqualTo(expected).Within(0.0001f));
    }

    [Test]
    public void GameEvent_IsLaidOutForTheClient() {
        var gameEvent = new GameEvent(new GameEventMetadata(990001, GameEventType.EnchantRate, DateTime.UnixEpoch,
            DateTime.UnixEpoch.AddSeconds(1000), TimeSpan.Zero, TimeSpan.Zero, [], new EnchantRate(14, 900), "14", "900", "", ""));
        Assert.That(gameEvent.SentToClient, Is.True);
        var writer = new ByteWriter();
        gameEvent.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer, 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("EnchantRate"));
        Assert.That(reader.ReadInt(), Is.EqualTo(990001)); // GameEvent::vfunc_4
        Assert.That(reader.ReadInt(), Is.EqualTo(14)); // the level (m_1c)
        Assert.That(reader.ReadInt(), Is.EqualTo(900)); // the raise (m_20)
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
    }

    private TestGameSession test = null!;

    private GameSession Start(params (int Id, EnchantRate Data, bool Over)[] events) {
        test = new TestGameSession();
        foreach ((int id, EnchantRate data, bool over) in events) {
            test.AddEvent(id, GameEventType.EnchantRate, data, over ? DateTime.Now.AddDays(-9) : null, over ? DateTime.Now.AddDays(-2) : null);
        }
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        return test.Start();
    }

    // The highest raise of the running events for the level; other levels and ended events are left out.
    [Test]
    public void Raise_HighestRunningForTheLevel() {
        GameSession session = Start((990001, new EnchantRate(14, 500), false), (990002, new EnchantRate(14, 900), false),
            (990003, new EnchantRate(13, 2000), false), (990004, new EnchantRate(14, 5000), true));
        IList<GameEvent> events = session.FindEvent(GameEventType.EnchantRate);

        Assert.That(EventSaleRules.EnchantRateRaise(events, 14), Is.EqualTo(900));
        Assert.That(EventSaleRules.EnchantRateRaise(events, 13), Is.EqualTo(2000));
        Assert.That(EventSaleRules.EnchantRateRaise(events, 12), Is.EqualTo(0));
    }

    // An item at +13 tries for 14 (base 10): an event for 14 raises the roll's rate, one for another level or an
    // ended one does not, and the staged item's packet keeps the base.
    [TestCase(14, 900, false, 100f)]
    [TestCase(14, 50, false, 15f)]
    [TestCase(13, 900, false, 10f)]
    [TestCase(14, 900, true, 10f)]
    public void OpheliaRate_RaisedForTheLevelTried(int level, int raise, bool over, float expected) {
        test = new TestGameSession();
        Item stored = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.AddEvent(990001, GameEventType.EnchantRate, new EnchantRate(level, raise), over ? DateTime.Now.AddDays(-9) : null,
            over ? DateTime.Now.AddDays(-2) : null);
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        GameSession session = test.Start();
        Item gear = session.Item.Inventory.Get(stored.Uid)!;
        gear.Enchant = new ItemEnchant(enchants: 13);

        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.ItemEnchant.OpheliaRate(), Is.EqualTo(expected).Within(0.0001f));
    }

    // Charges (1 each) fill only up to 100 from the raised rate: none is taken when the event already gives 100.
    [TestCase(900, 0f, 100f)]
    [TestCase(800, 10f, 100f)]
    [TestCase(0, 20f, 30f)]
    public void Charges_CappedOnTheRaisedRate(int raise, float charge, float expected) {
        test = new TestGameSession();
        Item stored = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        if (raise > 0) {
            test.AddEvent(990001, GameEventType.EnchantRate, new EnchantRate(14, raise));
        }
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        GameSession session = test.Start();
        Item gear = session.Item.Inventory.Get(stored.Uid)!;
        gear.Enchant = new ItemEnchant(enchants: 13, charges: 20);

        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);
        Assert.That(session.ItemEnchant.UpdateCharges(20), Is.True);
        Assert.That(test.Errors, Is.Empty);
        Assert.That(session.ItemEnchant.OpheliaRate(), Is.EqualTo(expected).Within(0.0001f));
        Assert.That(session.ItemEnchant.OpheliaRate() - EventSaleRules.EnchantSuccessWithEvent(10, raise), Is.EqualTo(charge).Within(0.0001f));
    }
}
