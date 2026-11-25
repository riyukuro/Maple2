using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class FunctionCubeUseRuleTests {
    // The client opens a FunctionUI cube's dialog only in state 2 or its move 5 (functioncubeobject.cpp:464-467).
    [Test]
    public void AFunctionUiCubeIsTakenToTheSecondState() {
        Assert.That(FunctionCubeUseRule.TakenState(InteractCubeControlType.FunctionUI), Is.EqualTo(InteractCubeState.Available));
        Assert.That(FunctionCubeUseRule.IsTaken(InteractCubeControlType.FunctionUI, InteractCubeState.Available), Is.True);
        Assert.That(FunctionCubeUseRule.IsTaken(InteractCubeControlType.FunctionUI, InteractCubeState.InUse), Is.False);
    }

    [TestCase(InteractCubeControlType.None)]
    [TestCase(InteractCubeControlType.Switch)]
    [TestCase(InteractCubeControlType.Skill)]
    [TestCase(InteractCubeControlType.Ride)]
    [TestCase(InteractCubeControlType.SpawnNPC)]
    [TestCase(InteractCubeControlType.InstallNPC)]
    public void AnyOtherCubeIsTakenToOne(InteractCubeControlType type) {
        Assert.That(FunctionCubeUseRule.TakenState(type), Is.EqualTo(InteractCubeState.InUse));
        Assert.That(FunctionCubeUseRule.IsTaken(type, InteractCubeState.InUse), Is.True);
    }
}
