using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game.Packets;

// FunctionCube modes 2, 3, 6, 9 and 10 against the client's function cube reader (functioncubeobject.cpp:2411).
// Modes 11-13 are in FunctionCubeNurturingTests.
public class FunctionCubeLayoutTests {
    private static byte[] Body(ByteWriter writer) => writer.Buffer.AsSpan(2, writer.Length - 2).ToArray();

    private static InteractCube Cube(string id, InteractCubeState state, Nurturing? nurturing = null) {
        var cube = (InteractCube) RuntimeHelpers.GetUninitializedObject(typeof(InteractCube));
        cube.Id = id;
        cube.State = state;
        cube.Nurturing = nurturing;
        return cube;
    }

    private static Nurturing Pet() {
        var pet = (Nurturing) RuntimeHelpers.GetUninitializedObject(typeof(Nurturing));
        typeof(Nurturing).GetProperty("CreationTime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(pet, DateTimeOffset.FromUnixTimeSeconds(0x01020304));
        pet.Exp = 0x0506;
        typeof(Nurturing).GetProperty(nameof(Nurturing.Stage))!.SetValue(pet, (short) 2);
        pet.ClaimedGiftForStage = 1;
        pet.LastFeedTime = DateTimeOffset.FromUnixTimeSeconds(0x0708);
        return pet;
    }

    private static FieldFunctionInteract Placed(InteractCube cube) {
        var entity = (FieldFunctionInteract) RuntimeHelpers.GetUninitializedObject(typeof(FieldFunctionInteract));
        typeof(FieldFunctionInteract).GetField(nameof(FieldFunctionInteract.InteractCube))!.SetValue(entity, cube);
        return entity;
    }

    // ReadCubeState: unicode id, int state (base vfunc_1), the nurturing class adds long, long, short, short, short,
    // long (CNurturingCubeObject::vfunc_1, :1784), then a byte.
    [Test]
    public void SendCubesAndAdd() {
        InteractCube plain = Cube("4_1", InteractCubeState.Available);
        InteractCube pet = Cube("4_2", InteractCubeState.None, Pet());

        var expected = new ByteWriter();
        expected.WriteByte(2);
        expected.WriteInt(2);
        expected.WriteUnicodeString("4_1");
        expected.WriteInt((int) InteractCubeState.Available);
        expected.WriteByte(0);
        expected.WriteUnicodeString("4_2");
        expected.WriteInt((int) InteractCubeState.None);
        expected.WriteLong(0x01020304);
        expected.WriteLong(0x0506);
        expected.WriteShort(2);
        expected.WriteShort(1);
        expected.WriteShort(0);
        expected.WriteLong(0x0708);
        expected.WriteByte(0);
        Assert.That(Body(FunctionCubePacket.SendCubes([Placed(plain), Placed(pet)])), Is.EqualTo(expected.ToArray()));

        var add = new ByteWriter();
        add.WriteByte(3);
        add.WriteUnicodeString("4_1");
        add.WriteInt((int) InteractCubeState.Available);
        add.WriteByte(0);
        Assert.That(Body(FunctionCubePacket.AddFunctionCube(plain)), Is.EqualTo(add.ToArray()));
        add.Buffer[add.Length - 1] = 1;
        Assert.That(Body(FunctionCubePacket.UpdateFunctionCube(plain)), Is.EqualTo(add.ToArray()));
    }

    // 6: long, unicode, byte in use. 9: long, unicode, long time, int count. 10: long, unicode, long time. The time
    // is the server's clock and is skipped.
    [Test]
    public void FurnitureAndLifeSkill() {
        InteractCube cube = Cube("4_1", InteractCubeState.InUse);
        var furniture = new ByteWriter();
        furniture.WriteByte(6);
        furniture.WriteLong(0x0102030405060708);
        furniture.WriteUnicodeString("4_1");
        furniture.WriteBool(true);
        Assert.That(Body(FunctionCubePacket.UseFurniture(0x0102030405060708, cube)), Is.EqualTo(furniture.ToArray()));

        var head = new ByteWriter();
        head.WriteLong(0x0102030405060708);
        head.WriteUnicodeString("4_1");
        int size = head.Length;

        byte[] success = Body(FunctionCubePacket.SuccessLifeSkill(0x0102030405060708, cube));
        Assert.That(success[0], Is.EqualTo(9));
        Assert.That(success[1..(1 + size)], Is.EqualTo(head.ToArray()));
        Assert.That(success.Length, Is.EqualTo(1 + size + 8 + 4));
        Assert.That(BitConverter.ToInt32(success, 1 + size + 8), Is.EqualTo(1));

        byte[] fail = Body(FunctionCubePacket.FailLifeSkill(0x0102030405060708, cube));
        Assert.That(fail[0], Is.EqualTo(10));
        Assert.That(fail[1..(1 + size)], Is.EqualTo(head.ToArray()));
        Assert.That(fail.Length, Is.EqualTo(1 + size + 8));
    }
}
