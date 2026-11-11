using System;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Util;

// The bytes are the client's sender: the mail send dialog (client/ms2/uimaillistdialog.cpp:1619),
// after the sub-type: receiver, title, text.
public class MailRulesTests {
    [Test]
    public void SendRequest_ThreeStringsThenTheLengthCheck() {
        var writer = new ByteWriter();
        writer.WriteUnicodeString("receiver");
        writer.WriteUnicodeString(new string('t', 101));
        writer.WriteUnicodeString("text");
        var reader = new ByteReader(writer.Buffer.AsSpan(0, writer.Length).ToArray(), 0);

        Assert.That(reader.ReadUnicodeString(), Is.EqualTo("receiver"));
        string title = reader.ReadUnicodeString();
        string content = reader.ReadUnicodeString();
        Assert.That(reader.Position, Is.EqualTo(writer.Length));
        Assert.That(MailRules.CanSend(title, content), Is.False);
    }

    [TestCase(0, 1, false)]
    [TestCase(1, 0, false)]
    [TestCase(1, 1, true)]
    [TestCase(100, 2000, true)]
    [TestCase(101, 1, false)]
    [TestCase(1, 2001, false)]
    public void CanSend_UsesTheClientsLimits(int titleLength, int contentLength, bool expected) {
        Assert.That(MailRules.CanSend(new string('a', titleLength), new string('b', contentLength)), Is.EqualTo(expected));
    }
}
