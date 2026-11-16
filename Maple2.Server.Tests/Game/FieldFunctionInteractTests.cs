using System;
using System.Linq;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// A placed function cube (a switch, a farm plot) in use goes back to Available AutoStateChangeTime after its use,
// and the field is shown the change; a cube in use cannot be used again. A notice cube never changes on its own.
public class FieldFunctionInteractTests {
    private const int ChangeTime = 5000;

    private TestGameSession test = null!;

    [SetUp]
    public void Field() {
        test = new TestGameSession();
        test.Start();
        test.TakeSent();
    }

    private FieldFunctionInteract CubeOf(InteractCubeControlType control, InteractCubeState state) {
        var metadata = new FunctionCubeMetadata(50400001, 0, default, InteractCubeState.Available, control, [], ChangeTime, null);
        var cube = new InteractCube(new Vector3B(1, 2, 3), metadata) { State = state };
        return new FieldFunctionInteract(test.Session.Field!, 77, cube, 9001);
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void AUse_PutsTheCubeInUse_AndASecondUseIsRefused() {
        FieldFunctionInteract cube = CubeOf(InteractCubeControlType.Switch, InteractCubeState.Available);

        Assert.That(cube.Use(), Is.True);
        Assert.That(cube.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(cube.Use(), Is.False);
    }

    [Test]
    public void ACubeInUse_IsAvailableAgainAfterItsChangeTime() {
        FieldFunctionInteract cube = CubeOf(InteractCubeControlType.Switch, InteractCubeState.Available);
        cube.Use();
        long used = test.Session.Field!.FieldTick;

        cube.Update(used + ChangeTime - 1);
        Assert.That(cube.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(test.TakeSent(), Is.Empty);

        cube.Update(used + ChangeTime);
        Assert.That(cube.InteractCube.State, Is.EqualTo(InteractCubeState.Available));
        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] { Hex(FunctionCubePacket.UpdateFunctionCube(cube.InteractCube)) }));
    }

    // An available cube, a cube of no state, and a notice cube stay as they are.
    [TestCase(InteractCubeControlType.Switch, InteractCubeState.Available)]
    [TestCase(InteractCubeControlType.Switch, InteractCubeState.None)]
    [TestCase(InteractCubeControlType.Notice, InteractCubeState.InUse)]
    public void Update_LeavesTheCube(InteractCubeControlType control, InteractCubeState state) {
        FieldFunctionInteract cube = CubeOf(control, state);

        cube.Update(long.MaxValue);

        Assert.That(cube.InteractCube.State, Is.EqualTo(state));
        Assert.That(test.TakeSent(), Is.Empty);
    }
}
