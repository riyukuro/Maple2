using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The build budget and helper answers of ResponseCube, as CSceneStateGame::OnResponseCube reads them.
public class CubeBuildBudgetPacketTests {
    private static ByteReader Open(ByteWriter writer, byte command) {
        var reader = new ByteReader(writer.Buffer, 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.ResponseCube));
        Assert.That(reader.ReadByte(), Is.EqualTo(command));
        return reader;
    }

    // case 0x35 (scenestategame.cpp:10592): long receiver account, long owner account, long meso, long merat.
    [Test]
    public void Grant_IsReceiverOwnerMesoMerat() {
        ByteWriter writer = CubePacket.GrantBuildPermissions(11, 22, 3000, 40);
        ByteReader reader = Open(writer, 0x35);
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(22));
        Assert.That(reader.ReadLong(), Is.EqualTo(3000));
        Assert.That(reader.ReadLong(), Is.EqualTo(40));
        Assert.That(writer.Length, Is.EqualTo(3 + 32));
    }

    [Test]
    public void Revoke_NamesNoOwner() {
        ByteWriter writer = CubePacket.GrantBuildPermissions(11);
        ByteReader reader = Open(writer, 0x35);
        Assert.That(reader.ReadLong(), Is.EqualTo(11));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(0));
    }

    // case 0x38: byte error; when 0, long meso, long merat.
    [Test]
    public void Budget_HasTheValuesOnlyWithoutAnError() {
        ByteWriter ok = CubePacket.UpdateBuildBudget(0, 3000, 40);
        ByteReader reader = Open(ok, 0x38);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(3000));
        Assert.That(reader.ReadLong(), Is.EqualTo(40));
        Assert.That(ok.Length, Is.EqualTo(3 + 1 + 16));

        ByteWriter error = CubePacket.UpdateBuildBudget(96);
        reader = Open(error, 0x38);
        Assert.That(reader.ReadByte(), Is.EqualTo(96));
        Assert.That(error.Length, Is.EqualTo(3 + 1));
    }

    // cases 0x39 and 0x3a: byte error; when 0, the long account id added or removed.
    [Test]
    public void AddAndRemove_HaveTheIdOnlyWithoutAnError() {
        ByteWriter add = CubePacket.AddBuildPermission(0, 77);
        ByteReader reader = Open(add, 0x39);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(add.Length, Is.EqualTo(3 + 1 + 8));
        Assert.That(CubePacket.AddBuildPermission(1).Length, Is.EqualTo(3 + 1));

        ByteWriter remove = CubePacket.RemoveBuildPermission(0, 77);
        reader = Open(remove, 0x3a);
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(remove.Length, Is.EqualTo(3 + 1 + 8));
        Assert.That(CubePacket.RemoveBuildPermission(1).Length, Is.EqualTo(3 + 1));
    }

    // case 0x3b: int count, count x long account id.
    [Test]
    public void List_IsCountThenIds() {
        ByteWriter writer = CubePacket.LoadBuildPermission(new List<long> { 7, 8, 9 });
        ByteReader reader = Open(writer, 0x3b);
        Assert.That(reader.ReadInt(), Is.EqualTo(3));
        Assert.That(reader.ReadLong(), Is.EqualTo(7));
        Assert.That(reader.ReadLong(), Is.EqualTo(8));
        Assert.That(reader.ReadLong(), Is.EqualTo(9));
        Assert.That(writer.Length, Is.EqualTo(3 + 4 + 24));

        Assert.That(CubePacket.LoadBuildPermission(new List<long>()).Length, Is.EqualTo(3 + 4));
    }
}
