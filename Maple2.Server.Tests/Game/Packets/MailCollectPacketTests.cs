using System;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game.Packets;

// CSceneStateGame::OnMail (client/ms2/scenestategame.cpp): mode 10 reads id, byte, slot byte, time; mode 11 reads
// id, time. The bytes checked here start after the opcode and the mode byte.
public class MailCollectPacketTests {
    private const long Now = 1_700_000_000;
    private const long MailId = 0x0102030405060708;

    private static byte[] Body(ByteWriter writer, byte mode) {
        byte[] bytes = writer.Buffer.AsSpan(0, writer.Length).ToArray();
        Assert.That(bytes[2], Is.EqualTo(mode));
        return bytes.AsSpan(3).ToArray();
    }

    [Test]
    public void CollectItem_NamesTheSlot() {
        byte[] body = Body(MailPacket.CollectItem(MailId, 3, Now), 10);
        Assert.That(body.Length, Is.EqualTo(18));
        Assert.That(BitConverter.ToInt64(body, 0), Is.EqualTo(MailId));
        Assert.That(body[8], Is.EqualTo(1));
        Assert.That(body[9], Is.EqualTo(3));
        Assert.That(BitConverter.ToInt64(body, 10), Is.EqualTo(Now));
    }

    [Test]
    public void CollectMeso_UsesTheZeroByte() {
        byte[] body = Body(MailPacket.CollectMeso(MailId, Now), 10);
        Assert.That(body.Length, Is.EqualTo(18));
        Assert.That(BitConverter.ToInt64(body, 0), Is.EqualTo(MailId));
        Assert.That(body[8], Is.EqualTo(0));
        Assert.That(body[9], Is.EqualTo(0));
        Assert.That(BitConverter.ToInt64(body, 10), Is.EqualTo(Now));
    }

    [Test]
    public void CollectRead_CarriesTheReadTime() {
        var mail = new Mail { Id = MailId, ReadTime = Now };
        byte[] body = Body(MailPacket.CollectRead(mail), 11);
        Assert.That(body.Length, Is.EqualTo(16));
        Assert.That(BitConverter.ToInt64(body, 0), Is.EqualTo(MailId));
        Assert.That(BitConverter.ToInt64(body, 8), Is.EqualTo(Now));
    }

    [Test]
    public void Notify_CountThenAlarmThenCount() {
        byte[] bytes = MailPacket.Notify(2, true).Buffer.AsSpan(0, 12).ToArray();
        Assert.That(bytes[2], Is.EqualTo(14));
        Assert.That(BitConverter.ToInt32(bytes, 3), Is.EqualTo(2));
        Assert.That(bytes[7], Is.EqualTo(1));
        Assert.That(BitConverter.ToInt32(bytes, 8), Is.EqualTo(2));
    }

    [Test]
    public void UnreadCount_LeavesOutReadAndExpiredMails() {
        Mail[] mails = [
            new Mail { Id = 1 },
            new Mail { Id = 2, ReadTime = Now - 5 },
            new Mail { Id = 3, ExpiryTime = Now },
            new Mail { Id = 4, ExpiryTime = Now + 1 },
        ];
        Assert.That(MailRules.UnreadCount(mails, Now), Is.EqualTo(2));

        mails[0].ReadTime = Now;
        Assert.That(MailRules.UnreadCount(mails, Now), Is.EqualTo(1));
    }

    [Test]
    public void CollectError_AlreadyReceivedThenExpired() {
        var meso = new Mail { Id = 1, Meso = 100 };
        Assert.That(MailRules.CollectError(meso, Now), Is.EqualTo(MailError.none));

        meso.ExpiryTime = Now + 1;
        Assert.That(MailRules.CollectError(meso, Now), Is.EqualTo(MailError.none));
        meso.ExpiryTime = Now;
        Assert.That(MailRules.CollectError(meso, Now), Is.EqualTo(MailError.s_mail_error_receive_expired));
        Assert.That((byte) MailError.s_mail_error_receive_expired, Is.EqualTo(21));

        meso.MesoCollectTime = Now - 1;
        Assert.That(MailRules.CollectError(meso, Now), Is.EqualTo(MailError.s_mail_error_already_receive));
    }

    [TestCase(MailError.s_mail_error_alreadyread, true)]
    [TestCase(MailError.s_mail_error_already_receive, true)]
    [TestCase(MailError.s_mail_error_receiveitem_to_inven, false)]
    [TestCase(MailError.s_mail_error_receive_expired, false)]
    [TestCase(MailError.mail_not_found, false)]
    public void BulkSkips_OnlyMailsAlreadyDone(MailError error, bool expected) {
        Assert.That(MailRules.BulkSkips(error), Is.EqualTo(expected));
    }
}
