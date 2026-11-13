using System;
using System.Collections.Generic;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game;

// SurvivalSkin (KR 20900009, 20910009, 20920006): a medal item (group 2, type 9) becomes a survival medal when it
// is added (InventoryManager.Add, SurvivalManager.AddMedal) and never reaches the bag, so the client has no use to
// send. A second medal of the same id renews the one held.
public class SurvivalSkinTests {
    private const int TailId = 20900001;
    private const int RidingId = 20910001;
    private TestGameSession test = null!;

    private GameSession Start() {
        test = new TestGameSession();
        test.SetTable(new SurvivalSkinInfoTable(new Dictionary<int, MedalType> {
            [TailId] = MedalType.Tail,
            [RidingId] = MedalType.Riding,
        }));
        GameSession session = test.Start();
        session.Survival = new SurvivalManager(session);
        test.TakeSent();
        return session;
    }

    private static Item Medal(int itemId, string parameters) {
        return new Item(TestGameSession.Metadata(itemId,
            function: new ItemMetadataFunction(ItemFunction.SurvivalSkin, "SurvivalSkin", parameters, false)));
    }

    // Before, the held medal's expiry had the new one's epoch added to it, so a second medal never expired.
    [Test]
    public void AddMedal_SecondEndDate_KeepsTheLaterEnd() {
        GameSession session = Start();
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-01-01-00-00-00\"/>"));
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2031-01-01-00-00-00\"/>"));
        Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.ExpiryTime, Is.EqualTo(new DateTime(2031, 1, 1).ToEpochSeconds()));
        session.Survival.AddMedal(Medal(20900009, $"<v id=\"{TailId}\" type=\"effectTail\" endDate=\"2030-06-01-00-00-00\"/>"));
        Assert.That(session.Survival.Find(MedalType.Tail, TailId)?.ExpiryTime, Is.EqualTo(new DateTime(2031, 1, 1).ToEpochSeconds()));
    }

    // A durationSec medal adds its time to what is left of the held one.
    [Test]
    public void AddMedal_SecondDuration_AddsItsTime() {
        GameSession session = Start();
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        session.Survival.AddMedal(Medal(20910009, $"<v id=\"{RidingId}\" type=\"riding\" durationSec=\"10080\"/>"));
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(session.Survival.Find(MedalType.Riding, RidingId)?.ExpiryTime, Is.InRange(before + 2 * 10080, after + 2 * 10080));
    }
}
