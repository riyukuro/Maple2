using System;
using System.Collections.Generic;
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

    // The box says ten seconds (s_home_password_user_out_chat_msg).
    [Test]
    public void SendOutWaitsTenSeconds() {
        Assert.That(HomeVisitorRules.SendOutDelay, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    private sealed class Entry { }

    [Test]
    public void TimerSendsOutVisitorsStillInTheHome() {
        Entry a = new(), b = new();
        var told = new Dictionary<int, Entry> { [11] = a, [12] = b };
        var inField = new Dictionary<int, Entry> { [11] = a, [12] = b, [13] = new Entry() };

        Assert.That(HomeVisitorRules.StillToSendOut(told, inField), Is.EquivalentTo(new[] { a, b }));
    }

    // Exit (RequestMoveField 1) or any other way out before the timer: nothing left to send out.
    [Test]
    public void TimerSkipsVisitorsThatAlreadyLeft() {
        Entry a = new(), b = new();
        var told = new Dictionary<int, Entry> { [11] = a, [12] = b };
        var inField = new Dictionary<int, Entry> { [12] = b };

        Assert.That(HomeVisitorRules.StillToSendOut(told, inField), Is.EquivalentTo(new[] { b }));
    }

    // Left and came back inside the ten seconds: the new entry (new object id, new field player) stays,
    // even if the home happens to hand out the old object id again.
    [Test]
    public void TimerSkipsVisitorThatLeftAndCameBack() {
        Entry first = new();
        var told = new Dictionary<int, Entry> { [11] = first };

        var backWithNewId = new Dictionary<int, Entry> { [14] = new Entry() };
        Assert.That(HomeVisitorRules.StillToSendOut(told, backWithNewId), Is.Empty);

        var backWithSameId = new Dictionary<int, Entry> { [11] = new Entry() };
        Assert.That(HomeVisitorRules.StillToSendOut(told, backWithSameId), Is.Empty);
    }
}
