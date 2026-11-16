using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Core.Formulas;

namespace Maple2.Server.Tests.Core;

// The base stats a character's level and job give (Stats.Reset), for every job at every level, and the weapon bonus
// attack coefficient (StatsManager).
public class BaseStatTests {
    private static readonly Func<JobCode, short, long>[] Growing = [
        BaseStat.Strength, BaseStat.Dexterity, BaseStat.Intelligence, BaseStat.Luck, BaseStat.Health,
        BaseStat.Defense, BaseStat.PhysicalRes, BaseStat.MagicalRes,
    ];
    private static readonly Func<JobCode, short, long>[] Flat = [
        BaseStat.Accuracy, BaseStat.Evasion, BaseStat.CriticalRate, BaseStat.CriticalDamage, BaseStat.CriticalEvasion,
    ];

    private static IEnumerable<JobCode> Jobs() => Enum.GetValues<JobCode>();

    // Each stat is positive from level 1 (resistances from level 1 up), never falls as the level rises, and the
    // fixed ones stay the same at every level.
    [TestCaseSource(nameof(Jobs))]
    public void EveryLevel_NeverFalls(JobCode job) {
        Assert.Multiple(() => {
            foreach (Func<JobCode, short, long> stat in Growing) {
                long last = 0;
                for (short level = 1; level <= 99; level++) {
                    long value = stat(job, level);
                    Assert.That(value, Is.GreaterThanOrEqualTo(last), $"{stat.Method.Name} {job} {level}");
                    last = value;
                }
                Assert.That(stat(job, 1), Is.Positive, $"{stat.Method.Name} {job}");
            }
            foreach (Func<JobCode, short, long> stat in Flat) {
                Assert.That(Enumerable.Range(1, 99).Select(level => stat(job, (short) level)).Distinct().Count(), Is.EqualTo(1), stat.Method.Name);
            }
        });
    }

    // A Knight's strength: 8, then 7 a level to 50, then 1 a level; a Wizard's intelligence leads its strength.
    [Test]
    public void PinnedValues() {
        Assert.Multiple(() => {
            Assert.That(BaseStat.Strength(JobCode.Knight, 1), Is.EqualTo(8));
            Assert.That(BaseStat.Strength(JobCode.Knight, 50), Is.EqualTo(8 + 49 * 7));
            Assert.That(BaseStat.Strength(JobCode.Knight, 99), Is.EqualTo(8 + 49 * 7 + 49));
            Assert.That(BaseStat.Intelligence(JobCode.Wizard, 60), Is.GreaterThan(BaseStat.Strength(JobCode.Wizard, 60)));
            Assert.That(BaseStat.Dexterity(JobCode.Archer, 60), Is.GreaterThan(BaseStat.Strength(JobCode.Archer, 60)));
            Assert.That(BaseStat.Luck(JobCode.Thief, 60), Is.GreaterThan(BaseStat.Intelligence(JobCode.Thief, 60)));
            Assert.That(BaseStat.Health(JobCode.Berserker, 50), Is.GreaterThan(BaseStat.Health(JobCode.Wizard, 50)));
            Assert.That(BaseStat.Accuracy(JobCode.Knight, 1), Is.EqualTo(82));
            Assert.That(BaseStat.CriticalDamage(JobCode.Knight, 1), Is.EqualTo(125));
            Assert.That(BaseStat.CriticalEvasion(JobCode.Knight, 1), Is.EqualTo(50));
            Assert.That(BaseStat.Defense(JobCode.Knight, 42), Is.EqualTo(42));
            Assert.That(BaseStat.PhysicalRes(JobCode.Knight, 99), Is.EqualTo(55));
            Assert.That(BaseStat.MagicalRes(JobCode.Priest, 99), Is.EqualTo(55));
            Assert.That(BaseStat.CriticalRate((JobCode) 12345, 1), Is.EqualTo(35));
        });
    }

    // Level 1 to 99 for a job the tables do not name: the defaults.
    [Test]
    public void UnnamedJob_Defaults() {
        var job = (JobCode) 12345;
        Assert.Multiple(() => {
            Assert.That(BaseStat.Strength(job, 99), Is.EqualTo(1));
            Assert.That(BaseStat.Evasion(job, 1), Is.EqualTo(70));
            Assert.That(BaseStat.PhysicalRes(job, 99), Is.EqualTo(25));
            Assert.That(BaseStat.MagicalRes(job, 99), Is.EqualTo(25));
        });
    }

    [Test]
    public void BonusAttack_NoWeapon_Zero() {
        Assert.That(BonusAttack.Coefficient(0, 5, JobCode.Knight), Is.Zero);
    }

    // One hand: 4.96 x the weapon's rarity factor x the job's; two hands average the two rarity factors.
    [TestCase(1, 0, 0.26)]
    [TestCase(2, 0, 0.27)]
    [TestCase(3, 0, 0.2883)]
    [TestCase(4, 0, 0.5)]
    [TestCase(5, 0, 1.0)]
    [TestCase(6, 0, 1.0)]
    [TestCase(7, 0, 0.0)]
    [TestCase(4, 6, 0.75)]
    public void BonusAttack_Rarity(int right, int left, double rarity) {
        Assert.That(BonusAttack.Coefficient(right, left, (JobCode) 12345), Is.EqualTo(4.96 * rarity).Within(1e-9));
    }

    [TestCaseSource(nameof(Jobs))]
    public void BonusAttack_JobFactor(JobCode job) {
        double factor = BonusAttack.Coefficient(5, 0, job) / 4.96;
        Assert.That(factor, Is.InRange(0.97, 1.4));
    }
}
