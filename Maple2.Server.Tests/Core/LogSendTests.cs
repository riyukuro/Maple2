using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Helpers;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;
using Maple2.Server.Tests.Login;

namespace Maple2.Server.Tests.Core;

// The client's log (LogSend, CBigDataLogSystem::SendLog): byte 0, byte 0 (log), the key and the value as unicode
// strings. A packet exception is sent with the key sock_exception and the value
// "CGameSocket::ProcessPacket <the exception's text>"; a socket read error's text names the op, the offset and the
// read, other exceptions' texts do not.
public class LogSendTests {
    private TestLoginSession test = null!;
    private LoginSession session = null!;
    private List<string> errors = null!;

    [SetUp]
    public void SetUp() {
        test = new TestLoginSession();
        session = test.Start();
        test.TakeSent();
        errors = [];
        session.OnError = (_, debug) => errors.Add(debug);
    }

    [TearDown]
    public void TearDown() {
        session.Dispose();
    }

    private void Log(string key, string value, byte first = 0) {
        new LogSendHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(first);
            writer.WriteByte(0);
            writer.WriteUnicodeString(key);
            writer.WriteUnicodeString(value);
        }));
    }

    [Test]
    public void PlainLog_Read() {
        Log("fps", "60");

        Assert.Multiple(() => {
            Assert.That(errors, Is.Empty);
            Assert.That(test.TakeSent(), Is.Empty);
        });
    }

    [Test]
    public void OtherFirstByte_Dropped() {
        Log("sock_exception", "[type=1][offset=2][hint=4]", first: 1);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void SocketReadError_Reported() {
        const string text = "CGameSocket::ProcessPacket [type=25][offset=14][hint=4]";
        Log("sock_exception", text);

        Assert.That(errors, Is.EqualTo(new[] { text }));
    }

    // An exception whose text is not a socket read error is still logged and reported.
    [TestCase("CGameSocket::ProcessPacket bad alloc")]
    [TestCase("")]
    public void OtherException_Reported(string text) {
        Assert.DoesNotThrow(() => Log("sock_exception", text));

        Assert.That(errors, Is.EqualTo(new[] { text }));
    }

    // Metrics: byte 0, byte 1, a count, then per entry the type and five numbers.
    [Test]
    public void Metrics_Read() {
        new LogSendHandler().Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteByte(1);
            writer.WriteByte(2);
            for (int i = 0; i < 2; i++) {
                writer.WriteString("latency");
                writer.WriteFloat(1);
                writer.WriteFloat(2);
                writer.WriteInt(3);
                writer.WriteFloat(4);
                writer.WriteFloat(5);
            }
        }));

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Parse_TheThreeFields() {
        SockExceptionInfo info = ErrorParserHelper.Parse("x [type=25][offset=14][hint=sa] y");

        Assert.That((info.SendOp, info.Offset, info.Hint), Is.EqualTo(((SendOp) 25, 14u, SockHint.DecodeStrA)));
    }

    [TestCase("no fields")]
    [TestCase("[type=1][offset=2]")]
    public void Parse_NotAReadError_ArgumentException(string text) {
        Assert.Throws<ArgumentException>(() => ErrorParserHelper.Parse(text));
    }

    private static IEnumerable<TestCaseData> Hints() {
        yield return new TestCaseData("1", SockHint.Decode1, "pWriter.WriteByte();", "07");
        yield return new TestCaseData("2", SockHint.Decode2, "pWriter.WriteShort();", "0700");
        yield return new TestCaseData("4", SockHint.Decode4, "pWriter.WriteInt();", "07000000");
        yield return new TestCaseData("f", SockHint.Decodef, "pWriter.WriteFloat();", "0000E040");
        yield return new TestCaseData("8", SockHint.Decode8, "pWriter.WriteLong();", "0700000000000000");
        yield return new TestCaseData("s", SockHint.DecodeStr, "pWriter.WriteUnicodeString(\"\");", "01003700");
        yield return new TestCaseData("sa", SockHint.DecodeStrA, "pWriter.WriteString(\"\");", "010037");
        yield return new TestCaseData("Decode4", SockHint.Decode4, "pWriter.WriteInt();", "07000000");
    }

    // The resolver's hints: the client's read name, the writer call it stands for, and the value it fills in.
    [TestCaseSource(nameof(Hints))]
    public void Hint_CodeAndFill(string text, SockHint hint, string code, string filled) {
        var writer = new ByteWriter();
        new SockHintInfo(text.ToSockHint(), "7").Update(writer);

        Assert.Multiple(() => {
            Assert.That(text.ToSockHint(), Is.EqualTo(hint));
            Assert.That(hint.GetCode(), Is.EqualTo(code));
            Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo(filled));
        });
    }

    // A zero string value fills an empty string.
    [Test]
    public void Hint_ZeroString_Empty() {
        var writer = new ByteWriter();
        new SockHintInfo(SockHint.DecodeStr, "0").Update(writer);

        Assert.That(Convert.ToHexString(writer.ToArray()), Is.EqualTo("0000"));
    }

    [Test]
    public void Hint_Unknown_Throws() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => ((SockHint) 99).GetCode());
            Assert.Throws<ArgumentException>(() => new SockHintInfo((SockHint) 99, "1").Update(new ByteWriter()));
        });
    }
}
