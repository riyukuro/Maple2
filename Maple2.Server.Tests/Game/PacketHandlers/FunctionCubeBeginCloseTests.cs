using System;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// FunctionCube 0x002E as the client writes it: mode 4 (StateHomemade::SendFunctionCubeMode4) is an i32 count and
// then (wstr cube id, u8) per cube, the farming and breeding cubes a gathering state begins on; mode 7 is the cube's
// id alone, sent when the dialog opened from the cube closes.
public class FunctionCubeBeginCloseTests {
    private const float Reach = 300;

    private TestGameSession test = null!;
    private GameSession session => test.Session;
    private FunctionCubeHandler handler = null!;

    private void Start() {
        test = new TestGameSession();
        test.Start();
        test.EnterHome();
        test.SetServerTable(new ConstantsTable(MasteryObjectInteractionDistance: (int) Reach));
        handler = new FunctionCubeHandler {
            FunctionCubeMetadataStorage = session.FunctionCubeMetadata,
            ServerTableMetadataStorage = test.ServerTableMetadata,
        };
    }

    private FieldFunctionInteract Cube(sbyte x, InteractCubeControlType type, float distance = 0,
                                       InteractCubeState defaultState = InteractCubeState.Available) {
        var cube = new InteractCube(new Vector3B(x, (sbyte) 0, (sbyte) 0), new FunctionCubeMetadata(50400000 + x, 0, ConfigurableCubeType.None,
            defaultState, type, [], 0, null));
        FieldFunctionInteract interact = test.AddFunctionCube(cube);
        interact.Position = session.Player.Position + new Vector3(distance, 0, 0);
        return interact;
    }

    private void Begin(params FieldFunctionInteract[] cubes) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(cubes.Length);
            foreach (FieldFunctionInteract cube in cubes) {
                writer.WriteUnicodeString(cube.InteractCube.Id);
                writer.WriteByte(1);
            }
        }));
    }

    private void Close(string id) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(7);
            writer.WriteUnicodeString(id);
        }));
    }

    // The begin keeps the farming and breeding cubes in reach and drops the names of the last begin; a cube out of
    // reach, a cube of another kind and a name the field does not have are not kept.
    [Test]
    public void Begin_KeepsTheGatheringCubesInReach() {
        Start();
        FieldFunctionInteract farm = Cube(1, InteractCubeControlType.Farming);
        FieldFunctionInteract breed = Cube(2, InteractCubeControlType.Breeding, Reach - 10);
        FieldFunctionInteract far = Cube(3, InteractCubeControlType.Farming, Reach + 500);
        FieldFunctionInteract chair = Cube(4, InteractCubeControlType.None);
        session.CubeStarted.Add("4_old");

        Begin(farm, breed, far, chair);

        Assert.That(session.CubeStarted, Is.EquivalentTo(new[] { farm.InteractCube.Id, breed.InteractCube.Id }));
        Assert.That(test.Errors, Is.Empty);
    }

    // A count over the client's reach list (32) or below 0 is not read and changes nothing.
    [TestCase(-1)]
    [TestCase(33)]
    public void Begin_CountOutOfRange_IsDropped(int count) {
        Start();
        session.CubeStarted.Add("4_old");

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteInt(count);
        }));

        Assert.That(session.CubeStarted, Is.EquivalentTo(new[] { "4_old" }));
    }

    // The closer holds the cube in use: it goes back to its default state and nobody holds it.
    [Test]
    public void Close_ByTheHolder_ReleasesTheCube() {
        Start();
        FieldFunctionInteract store = Cube(5, InteractCubeControlType.FunctionUI);
        store.InteractCube.State = InteractCubeState.InUse;
        store.InteractCube.InteractingCharacterId = session.CharacterId;

        Close(store.InteractCube.Id);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.Available));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // Another player's cube in use, a switch (no dialog) and a name the field does not have are left as they are.
    [Test]
    public void Close_NotTheHolder_LeavesTheCube() {
        Start();
        FieldFunctionInteract store = Cube(6, InteractCubeControlType.FunctionUI);
        store.InteractCube.State = InteractCubeState.InUse;
        store.InteractCube.InteractingCharacterId = session.CharacterId + 1;
        FieldFunctionInteract lever = Cube(7, InteractCubeControlType.Switch);
        lever.InteractCube.State = InteractCubeState.InUse;
        lever.InteractCube.InteractingCharacterId = session.CharacterId;
        test.TakeSent();

        Close(store.InteractCube.Id);
        Close(lever.InteractCube.Id);
        Close("4_999");

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(session.CharacterId + 1));
        Assert.That(lever.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube), Is.Empty);
    }
}
