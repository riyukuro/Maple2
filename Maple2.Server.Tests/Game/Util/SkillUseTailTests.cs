using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The bytes are the client's sender: the skill use payload writer
// (unnamed/unit_1401f9eb0_packetpayloadserializers.cpp:1057), from the field after the cooldown float on.
public class SkillUseTailTests {
    private static byte[] ClientBytes(int clientTick, bool flag, long itemUid, long cooldownKey, bool attached, int attachId, string node) {
        var writer = new ByteWriter();
        writer.WriteInt(clientTick);
        writer.WriteBool(flag);
        writer.WriteLong(itemUid);
        writer.WriteLong(cooldownKey);
        writer.WriteBool(attached);
        if (attached) {
            writer.WriteInt(attachId);
            writer.WriteUnicodeString(node);
        }
        return writer.Buffer.AsSpan(0, writer.Length).ToArray();
    }

    [Test]
    public void Read_CooldownKeyIsNotTheAttachFlag() {
        // A cooldown key with a non-zero low byte used to be read as the attach flag, id and name length.
        byte[] bytes = ClientBytes(1234, true, 77, 0x0102030405060708, false, 0, "");
        Assert.That(bytes, Has.Length.EqualTo(22));

        var reader = new ByteReader(bytes, 0);
        SkillUseTail tail = SkillUseTail.Read(reader);

        Assert.That(reader.Position, Is.EqualTo(bytes.Length));
        Assert.That(tail.ClientTick, Is.EqualTo(1234));
        Assert.That(tail.Unknown, Is.True);
        Assert.That(tail.ItemUid, Is.EqualTo(77));
        Assert.That(tail.CooldownKey, Is.EqualTo(0x0102030405060708));
        Assert.That(tail.IsHold, Is.False);
        Assert.That(tail.HoldString, Is.Empty);
    }

    [Test]
    public void Read_AttachedSkillKeepsIdAndNode() {
        byte[] bytes = ClientBytes(5, false, 0, 0x7fffffffffffff01, true, 42, "node");
        Assert.That(bytes, Has.Length.EqualTo(36));

        var reader = new ByteReader(bytes, 0);
        SkillUseTail tail = SkillUseTail.Read(reader);

        Assert.That(reader.Position, Is.EqualTo(bytes.Length));
        Assert.That(tail.IsHold, Is.True);
        Assert.That(tail.HoldInt, Is.EqualTo(42));
        Assert.That(tail.HoldString, Is.EqualTo("node"));
    }
}
