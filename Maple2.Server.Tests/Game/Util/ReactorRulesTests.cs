using System.Numerics;
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
}
