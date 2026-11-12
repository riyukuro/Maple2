using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// Debuffs on a player shortened by special attributes 34 (stun) and 36 (debuff), with the values KMS2 sends.
public class BuffDurationReductionTests {
    private static AdditionalEffectMetadataProperty Property(BuffType type, BuffSubType subType, int stun) {
        return new AdditionalEffectMetadataProperty(type, subType, BuffCategory.None, BuffEventType.None, 0,
            1000, 0, 0, 1, false, false, false, false, false, false, false, 0, 0, 0, 0, stun,
            BuffKeepCondition.TimerDuration, BuffResetCondition.ResetEndTick, BuffDotCondition.Default, 0);
    }

    [Test]
    public void StunReductionShortensMotionDebuffs() {
        AdditionalEffectMetadataProperty stun = Property(BuffType.Debuff, BuffSubType.Motion, 1);
        AdditionalEffectMetadataProperty noStun = Property(BuffType.Debuff, BuffSubType.Motion, 0);
        Assert.That(Buff.ReducedDuration(stun, 4000, 15, 0), Is.EqualTo(3940));
        Assert.That(Buff.ReducedDuration(noStun, 1000, 15, 0), Is.EqualTo(985));
        Assert.That(Buff.ReducedDuration(stun, 2000, 5, 0), Is.EqualTo(1990));
        Assert.That(Buff.ReducedDuration(stun, 1500, 5, 0), Is.EqualTo(1492));
        Assert.That(Buff.ReducedDuration(stun, 4000, 0, 15), Is.EqualTo(4000));
    }

    [Test]
    public void DebuffReductionShortensStatusDebuffs() {
        AdditionalEffectMetadataProperty status = Property(BuffType.Debuff, BuffSubType.Status, 0);
        Assert.That(Buff.ReducedDuration(status, 5000, 0, 15), Is.EqualTo(4925));
        Assert.That(Buff.ReducedDuration(status, 8000, 0, 15), Is.EqualTo(7880));
        Assert.That(Buff.ReducedDuration(status, 9999000, 0, 28), Is.EqualTo(9719028));
        Assert.That(Buff.ReducedDuration(status, 16000, 15, 0), Is.EqualTo(16000));
    }

    [Test]
    public void OtherBuffsKeepTheirDuration() {
        Assert.That(Buff.ReducedDuration(Property(BuffType.Debuff, BuffSubType.Motion, 2), 1000, 15, 15), Is.EqualTo(1000));
        Assert.That(Buff.ReducedDuration(Property(BuffType.Debuff, BuffSubType.Motion, 4), 1000, 5, 15), Is.EqualTo(1000));
        Assert.That(Buff.ReducedDuration(Property(BuffType.Buff, BuffSubType.Status, 0), 5000, 15, 15), Is.EqualTo(5000));
        Assert.That(Buff.ReducedDuration(Property(BuffType.Debuff, BuffSubType.Status, 0), 0, 15, 15), Is.EqualTo(0));
        Assert.That(Buff.ReducedDuration(Property(BuffType.Debuff, BuffSubType.Status, 0), 5000, 0, 2000), Is.EqualTo(0));
    }
}
