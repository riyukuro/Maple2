using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Game.Packets;

public class MailExpiryLayoutTests {
    private const long Now = 1_700_000_000;

    // CMailInfo::Read ends with the read time, the expiry (m_n108, tested by IsExpired), the send time and the wedding xml.
    private static long WrittenExpiry(Mail mail) {
        var writer = new ByteWriter();
        mail.WriteTo(writer);
        byte[] bytes = writer.Buffer.AsSpan(0, writer.Length).ToArray();
        int tail = bytes.Length - 2 - 24; // empty unicode string = one short length
        Assert.That(BitConverter.ToInt16(bytes, bytes.Length - 2), Is.EqualTo(0));
        Assert.That(BitConverter.ToInt64(bytes, tail + 16), Is.EqualTo(mail.SendTime));
        return BitConverter.ToInt64(bytes, tail + 8);
    }

    [Test]
    public void SystemMailWithoutExpiryGetsTheConstant() {
        var mail = new Mail { Type = MailType.System, Content = "40000010", SendTime = Now };
        Assert.That(WrittenExpiry(mail), Is.EqualTo(0));

        mail.SetDefaultExpiry(30, Now);
        Assert.That(mail.ExpiryTime, Is.EqualTo(Now + 30 * 86400L));
        Assert.That(WrittenExpiry(mail), Is.EqualTo(Now + 30 * 86400L));
    }

    [Test]
    public void SetExpiryIsKept() {
        var mail = new Mail { Type = MailType.Player, ExpiryTime = Now + 60 };
        mail.SetDefaultExpiry(30, Now);
        Assert.That(mail.ExpiryTime, Is.EqualTo(Now + 60));

        var unset = new Mail();
        unset.SetDefaultExpiry(0, Now);
        Assert.That(unset.ExpiryTime, Is.EqualTo(0));
    }
}
