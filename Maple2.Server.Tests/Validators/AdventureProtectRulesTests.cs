using Maple2.Model.Validators;

namespace Maple2.Server.Tests.Validators;

public class AdventureProtectRulesTests {
    private const long MADE = 1_700_000_000;
    private const long HOURS_72 = 72 * 3600L;

    // KR constants: level 30, quest 50001605, 72 hours.
    [TestCase(30, true, HOURS_72, 7)]
    [TestCase(29, true, HOURS_72, 6)]
    [TestCase(30, false, HOURS_72, 5)]
    [TestCase(30, true, HOURS_72 - 1, 3)]
    [TestCase(1, false, 0, 0)]
    public void State_IsOneBitPerCondition(int level, bool questDone, long age, int expected) {
        byte state = AdventureProtectRules.State(level, questDone, MADE, MADE + age, 30, 50001605, 72);

        Assert.That(state, Is.EqualTo(expected));
    }

    [Test]
    public void State_NoConstantsIsCleared() {
        Assert.That(AdventureProtectRules.State(1, false, MADE, MADE, 0, 0, 0), Is.EqualTo(AdventureProtectRules.Cleared));
    }

    [Test]
    public void AgeRemaining_CountsDownToZero() {
        Assert.That(AdventureProtectRules.AgeRemaining(MADE, MADE + 3600, 72), Is.EqualTo(HOURS_72 - 3600));
        Assert.That(AdventureProtectRules.AgeRemaining(MADE, MADE + HOURS_72 + 5, 72), Is.EqualTo(0));
    }
}
