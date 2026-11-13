using System.Collections.Generic;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Util;

// An effect's skill with a linkSkillID takes the caster's learned level of the linked skill (Holy Place's buff
// 10400291 follows Healing Prayer 10400071), else the level the data names.
public class LinkedSkillLevelTests {
    private static readonly Dictionary<int, short> Learned = new() {
        [10400071] = 5,
        [10400301] = 0,
    };

    private static short? Lookup(int id) => Learned.TryGetValue(id, out short level) ? level : null;

    [Test]
    public void NoLink_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 0, Lookup), Is.EqualTo(1));
    }

    [Test]
    public void LearnedLink_TakesTheLearnedLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 10400071, Lookup), Is.EqualTo(5));
    }

    [Test]
    public void UnlearnedLink_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(1, 10400301, Lookup), Is.EqualTo(1));
    }

    [Test]
    public void CasterWithoutTheSkill_KeepsTheDataLevel() {
        Assert.That(SkillCastRules.LinkedLevel(2, 10400281, Lookup), Is.EqualTo(2));
        Assert.That(SkillCastRules.LinkedLevel(2, 10400281, _ => null), Is.EqualTo(2));
    }
}
