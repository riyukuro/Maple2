using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class HomeVisitorRulesTests {
    [TestCase((byte) 1, false, HomeVisitorRules.Action.Kill)]
    [TestCase((byte) 1, true, HomeVisitorRules.Action.None)]
    [TestCase((byte) 2, false, HomeVisitorRules.Action.Kick)]
    [TestCase((byte) 2, true, HomeVisitorRules.Action.Kick)]
    [TestCase((byte) 3, true, HomeVisitorRules.Action.Revive)]
    [TestCase((byte) 3, false, HomeVisitorRules.Action.None)]
    [TestCase((byte) 4, false, HomeVisitorRules.Action.None)]
    public void OwnerActsOnVisitor(byte command, bool dead, HomeVisitorRules.Action expected) {
        Assert.That(HomeVisitorRules.Pick(command, true, true, false, dead), Is.EqualTo(expected));
    }

    [TestCase((byte) 1)]
    [TestCase((byte) 2)]
    [TestCase((byte) 3)]
    public void RefusedForNonOwnerAbsentTargetOrSelf(byte command) {
        bool dead = command == 3;
        Assert.That(HomeVisitorRules.Pick(command, false, true, false, dead), Is.EqualTo(HomeVisitorRules.Action.None));
        Assert.That(HomeVisitorRules.Pick(command, true, false, false, dead), Is.EqualTo(HomeVisitorRules.Action.None));
        Assert.That(HomeVisitorRules.Pick(command, true, true, true, dead), Is.EqualTo(HomeVisitorRules.Action.None));
    }

    [Test]
    public void SendEveryoneOutMovesOnlyOtherAccountsAndOnlyForTheOwner() {
        Assert.That(HomeVisitorRules.SentOutWithAll(7, 7, 9), Is.True);
        Assert.That(HomeVisitorRules.SentOutWithAll(7, 7, 7), Is.False);
        Assert.That(HomeVisitorRules.SentOutWithAll(7, 9, 9), Is.False);
        Assert.That(HomeVisitorRules.SentOutWithAll(7, 9, 11), Is.False);
    }
}
