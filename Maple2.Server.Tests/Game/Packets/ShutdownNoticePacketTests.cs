using System;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.World.Service;

namespace Maple2.Server.Tests.Game.Packets;

public class ShutdownNoticePacketTests {
    private static string Hex(Maple2.PacketLib.Tools.ByteWriter? packet) => Convert.ToHexString(packet!.ToArray());

    [Test]
    public void Layouts_FollowTheClientsReads() {
        Assert.Multiple(() => {
            // 0xAF, mode 0, kind 0, int64 time.
            Assert.That(Hex(ShutdownNoticePacket.Time(0x0102030405)), Is.EqualTo("AF00" + "00" + "00" + "0504030201000000"));
            // mode 0 kind 1, mode 1 kind 1 and mode 1 kind 0, each with a unicode string.
            Assert.That(Hex(ShutdownNoticePacket.Quit("A")), Is.EqualTo("AF00" + "00" + "01" + "0100" + "4100"));
            Assert.That(Hex(ShutdownNoticePacket.Maintenance("A")), Is.EqualTo("AF00" + "01" + "01" + "0100" + "4100"));
            Assert.That(Hex(ShutdownNoticePacket.Notice("A")), Is.EqualTo("AF00" + "01" + "00" + "0100" + "4100"));
        });
    }

    [Test]
    public void QuitKinds_LeaveStaffConnected() {
        var quit = new AdminRequest.Types.ShutdownNotice { Kind = 1, Message = "A" };
        var maintenance = new AdminRequest.Types.ShutdownNotice { Kind = 2, Message = "A" };
        Assert.Multiple(() => {
            Assert.That(Hex(ChannelService.ShutdownNoticePacketFor(quit, false)), Is.EqualTo(Hex(ShutdownNoticePacket.Quit("A"))));
            Assert.That(Hex(ChannelService.ShutdownNoticePacketFor(quit, true)), Is.EqualTo(Hex(ShutdownNoticePacket.Notice("A"))));
            Assert.That(Hex(ChannelService.ShutdownNoticePacketFor(maintenance, false)), Is.EqualTo(Hex(ShutdownNoticePacket.Maintenance("A"))));
            Assert.That(Hex(ChannelService.ShutdownNoticePacketFor(maintenance, true)), Is.EqualTo(Hex(ShutdownNoticePacket.Notice("A"))));
            Assert.That(Hex(ChannelService.ShutdownNoticePacketFor(new AdminRequest.Types.ShutdownNotice { Kind = 0, Time = 5 }, true)),
                Is.EqualTo(Hex(ShutdownNoticePacket.Time(5))));
            Assert.That(ChannelService.ShutdownNoticePacketFor(new AdminRequest.Types.ShutdownNotice { Kind = 9 }, false), Is.Null);
        });
    }
}
