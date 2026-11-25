using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

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
        typeof(GameSession).GetField("<SeatRelease>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(session, new SeatReleaseEcho());
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

    // The closer holds the cube in use: it goes back to its default state and nobody holds it. A FunctionUI cube has
    // DefaultState 1 in the data (all 19, e.g. the bank 60000001) and is held in state 2.
    [Test]
    public void Close_ByTheHolder_ReleasesTheCube() {
        Start();
        FieldFunctionInteract store = Cube(5, InteractCubeControlType.FunctionUI, defaultState: InteractCubeState.InUse);
        store.InteractCube.State = InteractCubeState.Available;
        store.InteractCube.InteractingCharacterId = session.CharacterId;

        Close(store.InteractCube.Id);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(0));
        Assert.That(test.Errors, Is.Empty);
    }

    // Another player's cube in use, a switch (no dialog) and a name the field does not have are left as they are.
    [Test]
    public void Close_NotTheHolder_LeavesTheCube() {
        Start();
        FieldFunctionInteract store = Cube(6, InteractCubeControlType.FunctionUI, defaultState: InteractCubeState.InUse);
        store.InteractCube.State = InteractCubeState.Available;
        store.InteractCube.InteractingCharacterId = session.CharacterId + 1;
        FieldFunctionInteract lever = Cube(7, InteractCubeControlType.Switch);
        lever.InteractCube.State = InteractCubeState.InUse;
        lever.InteractCube.InteractingCharacterId = session.CharacterId;
        test.TakeSent();

        Close(store.InteractCube.Id);
        Close(lever.InteractCube.Id);
        Close("4_999");

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.Available));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(session.CharacterId + 1));
        Assert.That(lever.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube), Is.Empty);
    }

    private void Use(FieldFunctionInteract cube) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(5);
            writer.WriteInt(1);
            writer.WriteUnicodeString(cube.InteractCube.Id);
            writer.WriteByte(1);
        }));
    }

    // FunctionCube 6 (UseFurniture): u8 6, i64 character, wstr cube id, then the flag the client opens the dialog on.
    private static bool UseFlag(byte[] packet) => packet[2] == 6 && packet[^1] == 1;

    // A bank cube (FunctionUI, DefaultState 1) used by the action key goes to state 2, held by the user, and the use reply
    // has its flag set: the client opens the dialog only in state 2 (OpenSubKindDialog, FUN_14122e6d0,
    // functioncubeobject.cpp:464-467). Before, a cube in its default state 1 read as held by nobody refused every use.
    [Test]
    public void Use_OfAFunctionUiCube_TakesItToTheSecondState() {
        Start();
        FieldFunctionInteract store = Cube(8, InteractCubeControlType.FunctionUI, defaultState: InteractCubeState.InUse);
        test.TakeSent();

        Use(store);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.Available));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(session.CharacterId));
        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube).ToList();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(UseFlag(sent[1]), Is.True);
    }

    // Its close then gives it back: state 1, nobody holds it, and the use reply's flag is clear.
    [Test]
    public void Close_AfterTheUse_GivesTheCubeBack() {
        Start();
        FieldFunctionInteract store = Cube(9, InteractCubeControlType.FunctionUI, defaultState: InteractCubeState.InUse);
        Use(store);
        test.TakeSent();

        Close(store.InteractCube.Id);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(0));
        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube).ToList();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(UseFlag(sent[1]), Is.False);
    }

    // A seat (default 0) is still taken to 1 with the flag set.
    [Test]
    public void Use_OfASeat_TakesItToOne() {
        Start();
        FieldFunctionInteract seat = Cube(10, InteractCubeControlType.Ride, defaultState: InteractCubeState.None);
        test.TakeSent();

        Use(seat);

        Assert.That(seat.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        List<byte[]> sent = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube).ToList();
        Assert.That(sent, Has.Count.EqualTo(2));
        Assert.That(UseFlag(sent[1]), Is.True);
    }

    // A placed bank cube stays in its default state 1 on the field's updates, and its use then opens the dialog.
    [Test]
    public void Update_LeavesAFunctionUiCubeInItsDefaultState() {
        Start();
        FieldFunctionInteract store = Cube(11, InteractCubeControlType.FunctionUI, defaultState: InteractCubeState.InUse);
        store.InteractCube.State = InteractCubeState.InUse;
        test.TakeSent();

        store.Update(long.MaxValue);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.InUse));
        Assert.That(test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.FunctionCube), Is.Empty);

        Use(store);

        Assert.That(store.InteractCube.State, Is.EqualTo(InteractCubeState.Available));
        Assert.That(store.InteractCube.InteractingCharacterId, Is.EqualTo(session.CharacterId));
    }
}
