using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class FunctionCubeCloseRuleTests {
    [Test]
    public void HolderReleasesTheCube() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.True);
    }

    [Test]
    public void AnotherPlayerDoesNot() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.InUse, InteractCubeState.Available, 5, 6), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.InUse, InteractCubeState.Available, 0, 0), Is.False);
    }

    [Test]
    public void CubeNotInUseStays() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.Available, InteractCubeState.Available, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.InUse, InteractCubeState.InUse, 5, 5), Is.False);
    }

    [Test]
    public void SwitchAndLifeSkillCubesStay() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Switch, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Farming, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Nurturing, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
    }
}
