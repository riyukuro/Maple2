using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The layouts are the client's reader, CSceneStateGame::OnEnterUgcMap.
public class EnterUgcMapPacketTests {
    private static ByteReader Body(ByteWriter writer) {
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);
        Assert.That(reader.Read<SendOp>(), Is.EqualTo(SendOp.EnterUgcMap));
        return reader;
    }

    private static void AssertMode(ByteWriter writer, byte mode) {
        ByteReader reader = Body(writer);
        Assert.That(reader.ReadByte(), Is.EqualTo(mode));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void MessageBoxModes_AreOneByte() {
        AssertMode(EnterUgcMapPacket.NoRoom(), 0);
        AssertMode(EnterUgcMapPacket.NotExistRoom(), 1);
        AssertMode(EnterUgcMapPacket.Expired(), 2);
        AssertMode(EnterUgcMapPacket.NotExistBuilding(), 5);
        AssertMode(EnterUgcMapPacket.CannotVisitThatCharacterHome(), 6);
        AssertMode(EnterUgcMapPacket.VisitFailed(), 7);
        AssertMode(EnterUgcMapPacket.DisabledMap(), 8);
        AssertMode(EnterUgcMapPacket.Dead(), 9);
        AssertMode(EnterUgcMapPacket.MyHome(), 10);
        AssertMode(EnterUgcMapPacket.Private(), 11);
    }

    [Test]
    public void RequestPassword_ReadsThreeIntsLongIntAndFlag() {
        ByteReader reader = Body(EnterUgcMapPacket.RequestPassword(0x1122334455667788));
        Assert.That(reader.ReadByte(), Is.EqualTo(3));
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(0x1122334455667788));
        reader.ReadInt();
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void WrongPassword_ReadsThreeIntsAndLong() {
        ByteReader reader = Body(EnterUgcMapPacket.WrongPassword(42));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        reader.ReadInt();
        reader.ReadInt();
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(42));
        Assert.That(reader.Available, Is.EqualTo(0));
    }
}
