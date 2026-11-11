using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Validators;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Validators;

public class NameChangeRulesTests {
    [TestCase(true, 0, false, false, NameChangeRules.Refusal.Invalid)]
    [TestCase(false, -1, false, false, NameChangeRules.Refusal.Invalid)]
    [TestCase(false, 1, false, false, NameChangeRules.Refusal.Invalid)]
    [TestCase(false, 13, false, false, NameChangeRules.Refusal.Invalid)]
    [TestCase(false, 6, true, true, NameChangeRules.Refusal.Banned)]
    [TestCase(false, 6, false, true, NameChangeRules.Refusal.Taken)]
    [TestCase(false, 2, false, false, NameChangeRules.Refusal.None)]
    [TestCase(false, 12, false, false, NameChangeRules.Refusal.None)]
    public void Check(bool blank, int width, bool banned, bool taken, NameChangeRules.Refusal expected) {
        Assert.That(NameChangeRules.Check(blank, width, 2, 12, banned, taken), Is.EqualTo(expected));
    }

    // The name check answers a taken name with its own failed flag: bool, i64 item uid, name.
    [Test]
    public void TakenAnswer() {
        var reader = new ByteReader(CheckCharacterNamePacket.Result(true, "Abc", 77).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.CheckCharNameResult));
        Assert.That(reader.ReadBool(), Is.True);
        Assert.That(reader.ReadLong(), Is.EqualTo(77));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Abc"));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // Every other refusal is a Notice message box with a client text that carries the name; none is a
    // CharacterCreate packet.
    [TestCase(NameChangeRules.Refusal.Invalid, 0x2c, 2)]
    [TestCase(NameChangeRules.Refusal.Banned, 0x29, 1)]
    [TestCase(NameChangeRules.Refusal.Taken, 0x2a, 1)]
    public void RefusalBox(NameChangeRules.Refusal refusal, int stringId, int argCount) {
        (StringCode code, string[] args) = NameChangeRules.Text(refusal, "Abc");
        var reader = new ByteReader(NoticePacket.MessageBox(new InterfaceText(code, args)).ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.Notice));
        Assert.That(reader.ReadByte(), Is.EqualTo(4));
        Assert.That(reader.ReadShort(), Is.EqualTo(64));
        Assert.That(reader.ReadBool(), Is.True);
        reader.ReadInt();
        Assert.That(reader.ReadInt(), Is.EqualTo(stringId));
        Assert.That(reader.ReadInt(), Is.EqualTo(argCount));
        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("Abc"));
    }
}
