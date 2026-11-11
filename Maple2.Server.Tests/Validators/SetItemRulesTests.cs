using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class SetItemRulesTests {
    private static SetBonusMetadata Part(int count, long strength = 0, params (int Id, short Level)[] effects) {
        var values = new Dictionary<BasicAttribute, long>();
        if (strength != 0) {
            values[BasicAttribute.Strength] = strength;
        }
        return new SetBonusMetadata(count, effects.Select(effect => new SetBonusAdditionalEffect(effect.Id, effect.Level)).ToArray(),
            values, new Dictionary<BasicAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>());
    }

    private static Dictionary<int, SetItemTable.Entry> Sets() {
        return new Dictionary<int, SetItemTable.Entry> {
            [801] = new(new SetItemInfoMetadata(801, "made-up", [9001, 9002, 9003, 9004], 801),
                [Part(2, 5), Part(3, 7, (0, 1)), Part(4, 9, (7001, 2), (7001, 2), (7002, 1))]),
        };
    }

    [Test]
    public void Counts_EveryWornItemOncePerSet() {
        // Two worn items of one id (the same set ids twice) count twice; an item of two sets counts in both.
        Dictionary<int, int> counts = SetItemRules.Counts([[801], [801], [801, 802], []]);

        Assert.That(counts[801], Is.EqualTo(3));
        Assert.That(counts[802], Is.EqualTo(1));
    }

    [Test]
    public void Counted_ExpiredItemIsNot() {
        Assert.That(SetItemRules.Expired(expiryTime: 0, now: 5000, pet: false), Is.False);
        Assert.That(SetItemRules.Expired(expiryTime: 5001, now: 5000, pet: false), Is.False);
        Assert.That(SetItemRules.Expired(expiryTime: 5000, now: 5000, pet: false), Is.True);
        Assert.That(SetItemRules.Expired(expiryTime: 4000, now: 5000, pet: true), Is.False);
        Assert.That(SetItemRules.Counted(expired: false, false, false, 0, 77, 0, 10), Is.True);
        Assert.That(SetItemRules.Counted(expired: true, false, false, 0, 77, 0, 10), Is.False);
    }

    [Test]
    public void Counted_UnusableItemIsNot() {
        // A premium-club item without the premium club; an item bound to another character.
        Assert.That(SetItemRules.Counted(false, requireVip: true, vip: false, 0, 77, 0, 10), Is.False);
        Assert.That(SetItemRules.Counted(false, requireVip: true, vip: true, 0, 77, 0, 10), Is.True);
        Assert.That(SetItemRules.Counted(false, false, false, boundTo: 78, characterId: 77, 0, 10), Is.False);
        Assert.That(SetItemRules.Counted(false, false, false, boundTo: 77, characterId: 77, 0, 10), Is.True);
    }

    [Test]
    public void Counted_OverLevelItemIsNot() {
        Assert.That(SetItemRules.Counted(false, false, false, 0, 77, levelLimit: 50, level: 49), Is.False);
        Assert.That(SetItemRules.Counted(false, false, false, 0, 77, levelLimit: 50, level: 50), Is.True);
    }

    [Test]
    public void SetKey_OriginIdWhenTheItemHasOne() {
        Assert.That(SetItemRules.SetKey(itemId: 7002, originId: 7001), Is.EqualTo(7001));
        Assert.That(SetItemRules.SetKey(itemId: 7002, originId: 0), Is.EqualTo(7002));
    }

    [Test]
    public void Active_AllPartsUpToTheCount() {
        SetBonusMetadata[] options = Sets()[801].Options;

        Assert.That(SetItemRules.Active(options, 1), Is.Empty);
        Assert.That(SetItemRules.Active(options, 3).Select(part => part.Count), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(SetItemRules.Active(options, 6).Select(part => part.Count), Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void Bonuses_LayersAreCountedApart() {
        // Two pieces as gear and two as outfit are two times "2 worn", not "4 worn"; an unknown set gives nothing.
        List<SetBonusMetadata> bonuses = SetItemRules.Bonuses([[[801], [801]], [[801], [801], [999]]], Sets());

        Assert.That(bonuses.Select(part => part.Count), Is.EqualTo(new[] { 2, 2 }));
    }

    [Test]
    public void Effects_OnceEach_EmptyEntrySkipped() {
        List<(int Id, short Level)> effects = SetItemRules.Effects(SetItemRules.Active(Sets()[801].Options, 4));

        Assert.That(effects, Is.EqualTo(new[] { (7001, (short) 2), (7002, (short) 1) }));
    }
}
