using Maple2.Model.Enum;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

public class FunctionCubeCloseRuleTests {
    // Every FunctionUI cube of the data has DefaultState 1 (InUse) and is taken to 2 (Available).
    [Test]
    public void HolderReleasesTheCube() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.Available, InteractCubeState.InUse, 5, 5), Is.True);
    }

    [Test]
    public void AnotherPlayerDoesNot() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.Available, InteractCubeState.InUse, 5, 6), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.Available, InteractCubeState.InUse, 0, 0), Is.False);
    }

    [Test]
    public void CubeNotInUseStays() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.InUse, InteractCubeState.InUse, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.FunctionUI, InteractCubeState.Available, InteractCubeState.Available, 5, 5), Is.False);
    }

    // A cube of another kind with a dialog close (a seat's default 0, taken to 1) is released as before.
    [Test]
    public void OtherCubeTakenToOneIsReleased() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Ride, InteractCubeState.InUse, InteractCubeState.None, 5, 5), Is.True);
    }

    [Test]
    public void SwitchAndLifeSkillCubesStay() {
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Switch, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Farming, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
        Assert.That(FunctionCubeCloseRule.Releases(InteractCubeControlType.Nurturing, InteractCubeState.InUse, InteractCubeState.Available, 5, 5), Is.False);
    }
}
