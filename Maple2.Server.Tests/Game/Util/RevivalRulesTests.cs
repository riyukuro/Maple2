using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// Revival mode 1, the meret revive in place: the price the client shows and the debuff limit.
public class RevivalRulesTests {
    [TestCase(true, 0, 2000000, true)]          // a revive in place
    [TestCase(false, 0, 2000000, true)]         // a safe revive at the map's spawn point
    [TestCase(false, 2000000, 2000000, true)]   // revivalReturnId is the map itself
    [TestCase(false, 2000001, 2000000, false)]  // sent to another map: its field entry gives the buffs
    public void RevivalRestoresLastingBuffs(bool instant, int returnId, int mapId, bool restores) {
        Assert.That(RevivalRules.RestoresLastingBuffs(instant, returnId, mapId), Is.EqualTo(restores));
    }

    [TestCase(true, 0, true)]
    [TestCase(false, 0, true)]
    [TestCase(false, 1, false)]
    [TestCase(false, 2759, false)]
    public void FieldEntryFillsHpOfADeadPlayer(bool leftDead, long hp, bool fills) {
        Assert.That(RevivalRules.FillsHpOnFieldEntry(leftDead, hp), Is.EqualTo(fills));
    }

    [Test]
    public void Cost_NoReduction_IsTheLuaPrice() {
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0f), Is.EqualTo(30));
        Assert.That(RevivalRules.MeretCost(30, 0f, 0.4f), Is.EqualTo(30));
    }

    [Test]
    public void Cost_ReductionIsCappedAndTruncated() {
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0.25f), Is.EqualTo(22));
        Assert.That(RevivalRules.MeretCost(30, 0.5f, 0.9f), Is.EqualTo(15));
        Assert.That(RevivalRules.MeretCost(20, 0.5f, 0.5f), Is.EqualTo(10));
    }

    [Test]
    public void Cost_NeverBelowZero() {
        Assert.That(RevivalRules.MeretCost(30, 2f, 1.5f), Is.EqualTo(0));
    }

    [Test]
    public void Debuff_OnThePlayer_Closes() {
        Assert.That(RevivalRules.DebuffAllows(90000001, false, false), Is.True);
        Assert.That(RevivalRules.DebuffAllows(90000001, true, false), Is.False);  // at any stack count
        Assert.That(RevivalRules.DebuffAllows(90000001, true, true), Is.True);    // infinityMeratRevival field
        Assert.That(RevivalRules.DebuffAllows(0, true, false), Is.True);          // no debuff row
    }
    [Test]
    public void DisableRevivalBuffBlocksReviveInPlace() {
        Assert.That(RevivalRules.BuffBlocks([]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, false]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, true]), Is.True);
    }

    // Revival mode 2, the revive in place for mesos or a voucher: one case per refusal, nothing is taken in any.
    private static MesoReviveRefusal Meso(bool dead = true, bool noRevivalHere = false, bool buffBlocks = false,
            bool useVoucher = false, bool hasVoucher = false, int usedToday = 0, int dailyMax = 10, long meso = 1000, long cost = 500,
            bool homeField = false) {
        return RevivalRules.MesoRevive(dead, noRevivalHere, buffBlocks, useVoucher, hasVoucher, usedToday, dailyMax, meso, cost,
            homeField: homeField);
    }

    [Test]
    public void MesoRevive_Allowed() {
        Assert.That(Meso(), Is.EqualTo(MesoReviveRefusal.None));
        Assert.That(Meso(meso: 500), Is.EqualTo(MesoReviveRefusal.None));
        Assert.That(Meso(usedToday: 9), Is.EqualTo(MesoReviveRefusal.None));
    }

    [Test]
    public void MesoRevive_ALivingPlayer_IsRefused() {
        Assert.That(Meso(dead: false), Is.EqualTo(MesoReviveRefusal.NotDead));
        Assert.That(Meso(dead: false, useVoucher: true, hasVoucher: true), Is.EqualTo(MesoReviveRefusal.NotDead));
    }

    [Test]
    public void MesoRevive_NoRevivalHereField_IsRefused() {
        Assert.That(Meso(noRevivalHere: true), Is.EqualTo(MesoReviveRefusal.NoRevivalHere));
        Assert.That(Meso(noRevivalHere: true, useVoucher: true, hasVoucher: true), Is.EqualTo(MesoReviveRefusal.NoRevivalHere));
    }

    [Test]
    public void MesoRevive_DisableRevivalBuff_IsRefused() {
        Assert.That(Meso(buffBlocks: true), Is.EqualTo(MesoReviveRefusal.Buff));
        Assert.That(Meso(buffBlocks: true, useVoucher: true, hasVoucher: true), Is.EqualTo(MesoReviveRefusal.Buff));
    }

    [Test]
    public void MesoRevive_DailyCountUsedUp_IsRefused() {
        Assert.That(Meso(usedToday: 10), Is.EqualTo(MesoReviveRefusal.DailyMax));
        Assert.That(Meso(usedToday: 11, meso: long.MaxValue), Is.EqualTo(MesoReviveRefusal.DailyMax));
    }

    [Test]
    public void MesoRevive_InAHome_IsFreeWithTheDailyCountUsedUp() {
        Assert.That(Meso(usedToday: 10, meso: 0, cost: 0, homeField: true), Is.EqualTo(MesoReviveRefusal.None));
        Assert.That(Meso(usedToday: 99, meso: 0, cost: 0, homeField: true), Is.EqualTo(MesoReviveRefusal.None));
        Assert.That(Meso(dead: false, homeField: true), Is.EqualTo(MesoReviveRefusal.NotDead));
        Assert.That(RevivalRules.CountsTowardDailyMax(homeField: true), Is.False);
        Assert.That(RevivalRules.CountsTowardDailyMax(homeField: false), Is.True);
    }

    [Test]
    public void MesoRevive_TooFewMesos_IsRefused() {
        Assert.That(Meso(meso: 499), Is.EqualTo(MesoReviveRefusal.LackMeso));
        Assert.That(Meso(meso: 0), Is.EqualTo(MesoReviveRefusal.LackMeso));
        Assert.That(Meso(cost: -1), Is.EqualTo(MesoReviveRefusal.LackMeso));
    }

    [Test]
    public void MesoRevive_Voucher() {
        Assert.That(Meso(useVoucher: true, hasVoucher: false), Is.EqualTo(MesoReviveRefusal.NoVoucher));
        // mesos held do not stand in for a missing voucher
        Assert.That(Meso(useVoucher: true, hasVoucher: false, meso: long.MaxValue), Is.EqualTo(MesoReviveRefusal.NoVoucher));
        // a voucher is not held to the daily count or to the meso balance
        Assert.That(Meso(useVoucher: true, hasVoucher: true, usedToday: 10, meso: 0), Is.EqualTo(MesoReviveRefusal.None));
    }

    [Test]
    public void MesoRevive_MeretDebuffAtItsTop_IsRefused() {
        Assert.That(RevivalRules.MesoRevive(true, false, false, false, false, 0, 10, 1000, 500, debuffAllows: false),
            Is.EqualTo(MesoReviveRefusal.DebuffMax));
        Assert.That(RevivalRules.MesoRevive(true, false, false, true, true, 0, 10, 1000, 500, debuffAllows: false),
            Is.EqualTo(MesoReviveRefusal.DebuffMax));
    }

    [Test]
    public void MesoCost_FollowsTheClientsButton() {
        Assert.That(RevivalRules.MesoCost(1000, 0.5f, 0f, false), Is.EqualTo(1000));
        Assert.That(RevivalRules.MesoCost(1000, 0.5f, 0.2f, false), Is.EqualTo(800));
        Assert.That(RevivalRules.MesoCost(1000, 0.5f, 0.9f, false), Is.EqualTo(500));   // capped by the limit
        Assert.That(RevivalRules.MesoCost(1000, 0f, 0.9f, false), Is.EqualTo(1000));    // a row of 0: no reduction
        Assert.That(new Maple2.Model.Metadata.ConstantsTable().MesoRevivalFeeReduceLimit, Is.EqualTo(0.5f)); // no row
        Assert.That(RevivalRules.MesoCost(1000, 0.5f, 0f, true), Is.EqualTo(0));        // free in a home
    }

    // Revival mode 1, the revive in place for merets or a coupon: one case per refusal, nothing is taken in any.
    private static MeretReviveRefusal Meret(bool dead = true, bool noRevivalHere = false, bool dungeonDisables = false,
            bool buffBlocks = false, bool localeClosed = false, bool debuffAllows = true, bool useCoupon = false,
            bool hasCoupon = false, long meret = 100, long cost = 30) {
        return RevivalRules.MeretRevive(dead, noRevivalHere, dungeonDisables, buffBlocks, localeClosed, debuffAllows,
            useCoupon, hasCoupon, meret, cost);
    }

    [Test]
    public void MeretRevive_Allowed() {
        Assert.That(Meret(), Is.EqualTo(MeretReviveRefusal.None));
        Assert.That(Meret(meret: 30), Is.EqualTo(MeretReviveRefusal.None));
        Assert.That(Meret(cost: 0, meret: 0), Is.EqualTo(MeretReviveRefusal.None));
    }

    [Test]
    public void MeretRevive_ALivingPlayer_IsRefused() {
        Assert.That(Meret(dead: false), Is.EqualTo(MeretReviveRefusal.NotDead));
        Assert.That(Meret(dead: false, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.NotDead));
    }

    [Test]
    public void MeretRevive_NoRevivalHereField_IsRefused() {
        Assert.That(Meret(noRevivalHere: true), Is.EqualTo(MeretReviveRefusal.NoRevivalHere));
        Assert.That(Meret(noRevivalHere: true, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.NoRevivalHere));
    }

    [Test]
    public void MeretRevive_DungeonThatDisablesIt_IsRefused() {
        Assert.That(Meret(dungeonDisables: true), Is.EqualTo(MeretReviveRefusal.Dungeon));
        Assert.That(Meret(dungeonDisables: true, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.Dungeon));
    }

    [Test]
    public void MeretRevive_DisableRevivalBuff_IsRefused() {
        Assert.That(Meret(buffBlocks: true), Is.EqualTo(MeretReviveRefusal.Buff));
        Assert.That(Meret(buffBlocks: true, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.Buff));
    }

    [Test]
    public void MeretRevive_ClosedForTheLocale_IsRefused() {
        Assert.That(Meret(localeClosed: true), Is.EqualTo(MeretReviveRefusal.Locale));
        Assert.That(Meret(localeClosed: true, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.Locale));
    }

    [Test]
    public void MeretRevive_DebuffAtItsTop_IsRefused() {
        Assert.That(Meret(debuffAllows: false), Is.EqualTo(MeretReviveRefusal.DebuffMax));
        Assert.That(Meret(debuffAllows: false, useCoupon: true, hasCoupon: true), Is.EqualTo(MeretReviveRefusal.DebuffMax));
    }

    [Test]
    public void MeretRevive_Coupon() {
        Assert.That(Meret(useCoupon: true, hasCoupon: false), Is.EqualTo(MeretReviveRefusal.NoCoupon));
        // merets held do not stand in for a missing coupon
        Assert.That(Meret(useCoupon: true, hasCoupon: false, meret: long.MaxValue), Is.EqualTo(MeretReviveRefusal.NoCoupon));
        // a coupon needs no merets
        Assert.That(Meret(useCoupon: true, hasCoupon: true, meret: 0), Is.EqualTo(MeretReviveRefusal.None));
    }

    [Test]
    public void MeretRevive_TooFewMerets_IsRefused() {
        Assert.That(Meret(meret: 29), Is.EqualTo(MeretReviveRefusal.LackMeret));
        Assert.That(Meret(meret: 0), Is.EqualTo(MeretReviveRefusal.LackMeret));
        Assert.That(Meret(cost: -1), Is.EqualTo(MeretReviveRefusal.LackMeret));
    }

    // Rows that ran out are dropped at the next death of any character; a missing constant counts as 180 s.
    [Test]
    public void DeathStreaks_ExpiredRowsAreDropped() {
        const long a = 9_100_000_000_001, b = 9_100_000_000_002, c = 9_100_000_000_003;
        const long t0 = 50_000_000_000_000; // later than every other death in these tests
        DeathStreaks.Count(a, t0, 180);
        DeathStreaks.Count(b, t0 + 100_000, 180);
        int before = DeathStreaks.Entries;
        Assert.That(DeathStreaks.Count(c, t0 + 181_000, 0), Is.EqualTo(1)); // a's row ran out, b's has not
        Assert.That(DeathStreaks.Entries, Is.LessThanOrEqualTo(before));
        Assert.That(DeathStreaks.Count(b, t0 + 200_000, 0), Is.EqualTo(2));
        Assert.That(DeathStreaks.Count(a, t0 + 201_000, 0), Is.EqualTo(1));
    }

    // The row is kept per character and follows it from field to field.
    [Test]
    public void DeathsInARow_PerCharacter() {
        const long a = 990000000131001, b = 990000000131002;
        Assert.That(DeathStreaks.Count(a, 1000, 180), Is.EqualTo(1));
        Assert.That(DeathStreaks.Count(a, 20000, 180), Is.EqualTo(2));
        Assert.That(DeathStreaks.Count(b, 21000, 180), Is.EqualTo(1));
        Assert.That(DeathStreaks.Count(a, 199000, 180), Is.EqualTo(3));
        Assert.That(DeathStreaks.Count(a, 379000, 180), Is.EqualTo(1));
    }
}
