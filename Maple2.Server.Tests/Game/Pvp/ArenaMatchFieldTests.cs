using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game.Pvp;

// The arena match of a field (ArenaMatchField): the units of a squad, the entry's window reset, the rewards by the
// zone's ranking (an item the data lacks is only logged), and the World told once when the match is over or empty.
public class ArenaMatchFieldTests {
    private const int Item1 = 40100023;
    private const int Item2 = 40199999;

    private static (TestGameSession Test, ArenaMatchField Match, List<int> Ended) Start(IndividualArenaTable.Entry arena) {
        var test = new TestGameSession();
        test.Start();
        test.Register(TestGameSession.Metadata(Item1, slotMax: 9999));
        var ended = new List<int>();
        var match = new ArenaMatchField(test.Session.Field!, 9, arena, [test.CharacterId], ended.Add);
        return (test, match, ended);
    }

    private static IndividualArenaTable.Entry Arena(int type = 2) => new(type, 65000002, 10, Item1, 100, 10, Item2, 5, 1);

    [Test]
    public void UnitOf_IsTheSquadsFirstMemberOrTheCharacterItself() {
        (_, ArenaMatchField match, _) = Start(Arena());
        match.Units = new Dictionary<long, long> { [5] = 4 };
        Assert.That(match.UnitOf(5), Is.EqualTo(4));
        Assert.That(match.UnitOf(6), Is.EqualTo(6));
    }

    [Test]
    public void Enter_OnlyAMatchCharacterOutsideSurvivalGetsTheWindowReset() {
        (TestGameSession test, ArenaMatchField match, _) = Start(Arena());
        test.TakeSent();
        match.Enter(test.Session.Player);
        Assert.That(test.TakeSent().Select(TestGameSession.Op), Does.Contain(SendOp.Pvp));

        var other = new TestGameSession(test.Options);
        other.Start();
        match.Enter(other.Session.Player);
        Assert.That(other.TakeSent().Select(TestGameSession.Op), Does.Not.Contain(SendOp.Pvp));
    }

    [Test]
    public void ZoneEnded_PaysWinnersAndLosersAndTellsTheWorldOnce() {
        (TestGameSession test, ArenaMatchField match, List<int> ended) = Start(Arena());
        // The ranking lists the character first (a winner) and a character not in the field, which is skipped.
        match.ZoneEnded([test.CharacterId, 999_999], 1);
        Assert.That(SpinWait.SpinUntil(() => ended.Count > 0, 2000), Is.True);
        Assert.That(test.Session.Item.Inventory.Find(Item1).Sum(item => item.Amount), Is.EqualTo(100));
        // The match is over: leaving and closing do not tell the World again.
        match.Close();
        Thread.Sleep(50);
        Assert.That(ended, Is.EqualTo(new[] { 9 }));

        // A loser gets the loser's count.
        (TestGameSession loser, ArenaMatchField second, _) = Start(Arena());
        second.ZoneEnded([999_999, loser.CharacterId], 1);
        Assert.That(loser.Session.Item.Inventory.Find(Item1).Sum(item => item.Amount), Is.EqualTo(10));
    }

    [Test]
    public void Left_TheWorldLetsGoOnlyWhenNoMatchCharacterIsLeft() {
        (TestGameSession test, ArenaMatchField match, List<int> ended) = Start(Arena());
        FieldManager field = test.Session.Field!;
        match.Left(field);
        Thread.Sleep(50);
        Assert.That(ended, Is.Empty);

        field.Players.TryRemove(test.Session.Player.ObjectId, out _);
        match.Left(field);
        Assert.That(SpinWait.SpinUntil(() => ended.Count > 0, 2000), Is.True);
        Assert.That(ended, Is.EqualTo(new[] { 9 }));
    }

    // The end call failing is logged and does not throw into the caller.
    [Test]
    public void TheWorldFailing_IsSwallowed() {
        var test = new TestGameSession();
        test.Start();
        int calls = 0;
        var match = new ArenaMatchField(test.Session.Field!, 3, Arena(), [test.CharacterId], _ => {
            calls++;
            throw new InvalidOperationException("world down");
        });
        match.Close();
        Assert.That(SpinWait.SpinUntil(() => calls > 0, 2000), Is.True);
        Assert.DoesNotThrow(match.Close);
    }
}
