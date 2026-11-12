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
    public void Debuff_AtTopStack_Closes() {
        Assert.That(RevivalRules.DebuffAllows(90000001, 2, 3, false), Is.True);
        Assert.That(RevivalRules.DebuffAllows(90000001, 3, 3, false), Is.False);
        Assert.That(RevivalRules.DebuffAllows(90000001, 3, 3, true), Is.True);
        Assert.That(RevivalRules.DebuffAllows(0, 3, 3, false), Is.True);
    }
    [Test]
    public void DisableRevivalBuffBlocksReviveInPlace() {
        Assert.That(RevivalRules.BuffBlocks([]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, false]), Is.False);
        Assert.That(RevivalRules.BuffBlocks([false, true]), Is.True);
    }

    // Revival mode 2, the revive in place for mesos or a voucher: one case per refusal, nothing is taken in any.
    private static MesoReviveRefusal Meso(bool dead = true, bool noRevivalHere = false, bool buffBlocks = false,
            bool useVoucher = false, bool hasVoucher = false, int usedToday = 0, int dailyMax = 10, long meso = 1000, long cost = 500) {
        return RevivalRules.MesoRevive(dead, noRevivalHere, buffBlocks, useVoucher, hasVoucher, usedToday, dailyMax, meso, cost);
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
