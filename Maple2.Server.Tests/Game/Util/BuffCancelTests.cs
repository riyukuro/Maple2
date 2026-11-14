using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Game.Util;

// CancelEffectProperty: which held buffs an effect cancels.
public class BuffCancelTests {
    private const int Caster = 1;
    private const int OtherCaster = 2;

    [Test]
    public void CodeCancelsTheBuffWhoeverGaveIt() {
        var cancel = new AdditionalEffectMetadataUpdate.CancelEffect(false, false, [10200243], []);
        Assert.That(Buff.Cancels(cancel, Caster, 10200243, BuffCategory.None, Caster), Is.True);
        Assert.That(Buff.Cancels(cancel, Caster, 10200243, BuffCategory.None, OtherCaster), Is.True);
        Assert.That(Buff.Cancels(cancel, Caster, 10200244, BuffCategory.None, Caster), Is.False);
    }

    // cancelCheckSameCaster: only a copy this effect's caster gave goes.
    [Test]
    public void SameCasterKeepsAnotherCastersCopy() {
        var cancel = new AdditionalEffectMetadataUpdate.CancelEffect(true, false, [10200243], []);
        Assert.That(Buff.Cancels(cancel, Caster, 10200243, BuffCategory.None, Caster), Is.True);
        Assert.That(Buff.Cancels(cancel, Caster, 10200243, BuffCategory.None, OtherCaster), Is.False);
    }

    // A cancel with categories and no codes (KR 90000070 and others) cancels by category.
    [Test]
    public void CategoriesAloneCancel() {
        var cancel = new AdditionalEffectMetadataUpdate.CancelEffect(false, false, [], [(BuffCategory) 2001]);
        Assert.That(Buff.Cancels(cancel, Caster, 50000001, (BuffCategory) 2001, OtherCaster), Is.True);
        Assert.That(Buff.Cancels(cancel, Caster, 50000001, BuffCategory.None, OtherCaster), Is.False);
    }
}
