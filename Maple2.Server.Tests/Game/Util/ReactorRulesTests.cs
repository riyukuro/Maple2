using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class ReactorRulesTests {
    private static ReactorTable.State State(ReactorStateType type, int next, int expire) {
        return new ReactorTable.State(1, type, next, expire, []);
    }

    [Test]
    public void Cell_RoundsLikeTheClient() {
        Assert.That(ReactorRules.Cell(new Vector3(74, 76, 299)), Is.EqualTo((0, 1, 1)));
        Assert.That(ReactorRules.Cell(new Vector3(-74, -76, -1)), Is.EqualTo((0, -1, -1)));
        Assert.That(ReactorRules.Cell(new Vector3(-300, 450, 150)), Is.EqualTo((-2, 3, 1)));
    }

    [Test]
    public void CellKey_PacksOneByteEach() {
        Assert.That(ReactorRules.CellKey((1, 2, 3)), Is.EqualTo(0x030201));
        Assert.That(ReactorRules.CellKey((-1, 0, 0)), Is.EqualTo(0xff));
        Assert.That(ReactorRules.Name(0x030201), Is.EqualTo("Reactor_197121"));
        Assert.That(ReactorRules.CellCenter((-2, 3, 1)), Is.EqualTo(new Vector3(-300, 450, 150)));
    }

    [Test]
    public void Tick_ConnectStateWaitsForItsExpiry() {
        ReactorTable.State connect = State(ReactorStateType.Connect, 0, 60);
        Assert.That(ReactorRules.Tick(connect, false, true, 59_999), Is.EqualTo(ReactorRules.Step.Stay));
        Assert.That(ReactorRules.Tick(connect, false, true, 60_000), Is.EqualTo(ReactorRules.Step.Advance));
        Assert.That(ReactorRules.Tick(connect, false, false, 60_000), Is.EqualTo(ReactorRules.Step.Remove));
    }

    [Test]
    public void Tick_NextTimeMovesOnAndTheLastStateRemoves() {
        ReactorTable.State sleep = State(ReactorStateType.Sleep, 15, 30);
        Assert.That(ReactorRules.Tick(sleep, false, true, 14_999), Is.EqualTo(ReactorRules.Step.Stay));
        Assert.That(ReactorRules.Tick(sleep, false, true, 15_000), Is.EqualTo(ReactorRules.Step.Advance));
        ReactorTable.State last = State(ReactorStateType.Reward, 1, 1);
        Assert.That(ReactorRules.Tick(last, true, false, 1_000), Is.EqualTo(ReactorRules.Step.Remove));
    }

    [Test]
    public void CanReact_FollowsTheStateType() {
        Assert.That(ReactorRules.CanReact(ReactorStateType.Connect, connected: false), Is.True);
        Assert.That(ReactorRules.CanReact(ReactorStateType.Connect, connected: true), Is.False);
        Assert.That(ReactorRules.CanReact(ReactorStateType.Reward, connected: true), Is.True);
        Assert.That(ReactorRules.CanReact(ReactorStateType.Reward, connected: false), Is.False);
        Assert.That(ReactorRules.CanReact(ReactorStateType.Sleep, connected: true), Is.False);
    }

    [Test]
    public void Needed_IsTheLargerOfConsumeAndCheck() {
        Assert.That(ReactorRules.Needed(new ReactorTable.ConditionItem(1, 1, 1, 3)), Is.EqualTo(3));
        Assert.That(ReactorRules.Needed(new ReactorTable.ConditionItem(1, 1, 2, 0)), Is.EqualTo(2));
    }

    [Test]
    public void Tick_AStateWithoutTimesIsRemovedAfterTheLimit() {
        ReactorTable.State state = State(ReactorStateType.Reward, 0, 0);
        Assert.That(ReactorRules.Tick(state, false, true, ReactorRules.NoTimeLimitMs - 1), Is.EqualTo(ReactorRules.Step.Stay));
        Assert.That(ReactorRules.Tick(state, false, true, ReactorRules.NoTimeLimitMs), Is.EqualTo(ReactorRules.Step.Remove));
    }

    [Test]
    public void IsClear_RefusesAPointInsideThePlacementDistance() {
        var center = new Vector3(150, 150, 0);
        Assert.That(ReactorRules.IsClear(center, [new Vector3(150, 379, 0)]), Is.False);
        Assert.That(ReactorRules.IsClear(center, [new Vector3(150, 380, 0)]), Is.True);
        Assert.That(ReactorRules.IsClear(center, []), Is.True);
    }

    [Test]
    public void InBox_TestsEachAxisAgainstHalfTheSize() {
        var position = new Vector3(0, 0, 0);
        var dimension = new Vector3(200, 100, 300);
        Assert.That(ReactorRules.InBox(new Vector3(100, 50, -150), position, dimension), Is.True);
        Assert.That(ReactorRules.InBox(new Vector3(101, 0, 0), position, dimension), Is.False);
        Assert.That(ReactorRules.InBox(new Vector3(0, 51, 0), position, dimension), Is.False);
    }

    [Test]
    public void Plan_AddsConditionsOnTheSameItem() {
        ReactorTable.ConditionItem[] conditions = [
            new ReactorTable.ConditionItem(1, 1, 1, 3),
            new ReactorTable.ConditionItem(1, 1, 2, 0),
            new ReactorTable.ConditionItem(0, 0, 5, 5),
        ];

        // Needs 3 + 2 held, takes 1 + 2.
        Assert.That(ReactorRules.Plan(conditions, [(10, 1, 1, 4)]), Is.Null);
        Assert.That(ReactorRules.Plan(conditions, [(10, 1, 1, 5)]), Is.EquivalentTo(new[] { (10L, 3) }));
    }

    [Test]
    public void Plan_RarityAndAnyRarityDoNotShareAStack() {
        ReactorTable.ConditionItem[] conditions = [
            new ReactorTable.ConditionItem(1, 0, 2, 0),
            new ReactorTable.ConditionItem(1, 4, 2, 0),
        ];

        // One stack of rarity 4 with 3: both conditions would see it; together they need 4.
        Assert.That(ReactorRules.Plan(conditions, [(10, 1, 4, 3)]), Is.Null);
        Assert.That(ReactorRules.Plan(conditions, [(10, 1, 4, 4)]), Is.EquivalentTo(new[] { (10L, 4) }));
        // The rarity condition picks first, so the any-rarity one is left the other stack.
        Assert.That(ReactorRules.Plan(conditions, [(10, 1, 4, 2), (11, 1, 1, 2)]), Is.EquivalentTo(new[] { (10L, 2), (11L, 2) }));
        Assert.That(ReactorRules.Plan(conditions, [(11, 1, 1, 9)]), Is.Null);
    }

    [Test]
    public void Plan_TakesAcrossStacksAndOnlyTheConsumePart() {
        ReactorTable.ConditionItem[] conditions = [new ReactorTable.ConditionItem(1, 0, 3, 5)];

        var plan = ReactorRules.Plan(conditions, [(10, 1, 1, 2), (11, 1, 1, 9), (12, 2, 1, 9)]);

        Assert.That(plan, Is.EquivalentTo(new[] { (10L, 2), (11L, 1) }));
    }

    [Test]
    public void ReactorEvent_IsSentOnlyWhenARowNamesIt() {
        var table = new ReactorTable(new Dictionary<int, ReactorTable.Entry> {
            [10000002] = new(10000002, 155, 1500, 900070, [], []),
            [10000009] = new(10000009, 155, 1500, 0, [], []),
        });
        Assert.That(ReactorRules.SendsEvent(GameEventType.reactor, 900070, table), Is.True);
        // The other locales' rows of the same reactor name these; sent, the last one would replace 900070.
        Assert.That(ReactorRules.SendsEvent(GameEventType.reactor, 900080, table), Is.False);
        Assert.That(ReactorRules.SendsEvent(GameEventType.reactor, 900112, table), Is.False);
        // Other event types do not depend on the table.
        Assert.That(ReactorRules.SendsEvent(GameEventType.StringBoard, 900080, table), Is.True);
    }
}
