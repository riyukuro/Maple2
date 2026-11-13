using System;
using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game.Packets;

// The layouts are the client's reader, CSceneStateGame::OnDpsStat.
public class DpsStatPacketTests {
    private static ByteReader Body(ByteWriter writer, out SendOp op) {
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);
        op = reader.Read<SendOp>();
        return reader;
    }

    [Test]
    public void Recover_WritesFlagTypeAndAmount() {
        ByteReader reader = Body(DpsStatPacket.Recover(true, BasicAttribute.Spirit, 15), out SendOp op);

        Assert.That(op, Is.EqualTo(SendOp.DpsStat));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(7));
        Assert.That(reader.ReadInt(), Is.EqualTo(15));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Recover_StaminaIsType10() {
        ByteReader reader = Body(DpsStatPacket.Recover(true, BasicAttribute.Stamina, 10), out _);

        reader.ReadByte();
        reader.ReadByte();
        Assert.That(reader.ReadInt(), Is.EqualTo(10));
    }

    [Test]
    public void Spend_WritesUnusedIntThenSpiritAndStamina() {
        ByteReader reader = Body(DpsStatPacket.Spend(12, 30), out SendOp op);

        Assert.That(op, Is.EqualTo(SendOp.DpsStat));
        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        Assert.That(reader.ReadInt(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(12));
        Assert.That(reader.ReadInt(), Is.EqualTo(30));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void HealEffective_GivenIsKind0_ReceivedIsKind1() {
        ByteReader given = Body(DpsStatPacket.HealEffective(false, 500), out _);
        Assert.That(given.ReadByte(), Is.EqualTo(2));
        Assert.That(given.ReadByte(), Is.EqualTo(0));
        Assert.That(given.ReadInt(), Is.EqualTo(500));
        Assert.That(given.Available, Is.EqualTo(0));

        ByteReader received = Body(DpsStatPacket.HealEffective(true, 499), out _);
        Assert.That(received.ReadByte(), Is.EqualTo(2));
        Assert.That(received.ReadByte(), Is.EqualTo(1));
        Assert.That(received.ReadInt(), Is.EqualTo(499));
    }
}
