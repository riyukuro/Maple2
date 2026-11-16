using System;
using System.Collections.Generic;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Core;

public class TimeSyncPacketTests {
    private static readonly DateTimeOffset Time = new(2026, 11, 11, 8, 16, 0, TimeSpan.Zero);

    [Test]
    public void ZoneIsTheSecondsWestOfUtc() {
        TimeZoneInfo kst = TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
        TimeZoneInfo cst = TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(-6), "CST", "CST");

        Assert.That(TimeSyncPacket.ZoneSeconds(Time, kst), Is.EqualTo(-32400));
        Assert.That(TimeSyncPacket.ZoneSeconds(Time, cst), Is.EqualTo(21600));
        Assert.That(TimeSyncPacket.ZoneSeconds(Time, TimeZoneInfo.Utc), Is.EqualTo(0));
    }

    [Test]
    public void ClientLocalTimeIsServerLocalTime() {
        TimeZoneInfo cst = TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(-6), "CST", "CST");
        // CMsTime::GetLocalTm: gmtime(utc - zone + dst * 3600), dst 0.
        DateTimeOffset client = Time.AddSeconds(-TimeSyncPacket.ZoneSeconds(Time, cst));

        Assert.That(client.DateTime, Is.EqualTo(TimeZoneInfo.ConvertTime(Time, cst).DateTime));
    }

    // ResponseTimeSync as CNetworkService::OnTimeSyncResponse (networkservice.cpp:756) reads it: byte kind; kind 0 is
    // int tick, long server time, int zone, byte daylight flag, int the request's sequence; kind 1 the same without the
    // sequence (the client then asks with RequestTimeSync); kind 2 is the kind alone (the client asks at once).
    private static ByteReader Body(ByteWriter packet, SendOp op) {
        var reader = new ByteReader(packet.ToArray(), 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) op));
        return reader;
    }

    [Test]
    public void Response_ReadsAsTheClient_AndEchoesTheSequence() {
        ByteReader reader = Body(TimeSyncPacket.Response(Time, 0x01020304), SendOp.ResponseTimeSync);

        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(Time.ToUnixTimeSeconds()));
        Assert.That(reader.ReadInt(), Is.EqualTo(TimeSyncPacket.ZoneSeconds(Time, TimeZoneInfo.Local)));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.ReadInt(), Is.EqualTo(0x01020304));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Reset_ReadsAsTheClient_WithoutASequence() {
        ByteReader reader = Body(TimeSyncPacket.Reset(Time), SendOp.ResponseTimeSync);

        Assert.That(reader.ReadByte(), Is.EqualTo(1));
        reader.ReadInt();
        Assert.That(reader.ReadLong(), Is.EqualTo(Time.ToUnixTimeSeconds()));
        Assert.That(reader.ReadInt(), Is.EqualTo(TimeSyncPacket.ZoneSeconds(Time, TimeZoneInfo.Local)));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    [Test]
    public void Request_IsTheKindAlone() {
        ByteReader reader = Body(TimeSyncPacket.Request(), SendOp.ResponseTimeSync);

        Assert.That(reader.ReadByte(), Is.EqualTo(2));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // RequestClientTickSync: OnRequestClientTickSync (networkservice.cpp:792) reads Unk_NetworkSyncRequest, one int
    // (MS2_CHECK_SIZE 4), and answers ResponseClientTickSync with it.
    [Test]
    public void TickSync_IsOneInt() {
        ByteReader reader = Body(RequestPacket.TickSync(0x0A0B0C0D), SendOp.RequestClientTickSync);

        Assert.That(reader.ReadInt(), Is.EqualTo(0x0A0B0C0D));
        Assert.That(reader.Available, Is.EqualTo(0));
    }

    // RequestTimeSync from the client (CNetworkService::SendTimeSyncRequest, networkservice.cpp:743): int sequence.
    // The game server answers kind 0 with that sequence, which the client matches to its sent time (m_latency).
    [Test]
    public void TimeSyncRequest_IsAnsweredWithItsSequence() {
        var test = new TestGameSession();
        test.Start();
        test.TakeSent();

        new Maple2.Server.Game.PacketHandlers.TimeSyncHandler().Handle(test.Session, TestGameSession.Packet(writer => writer.WriteInt(77)));

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent, Has.Count.EqualTo(1));
        var reader = new ByteReader(sent[0], 0);
        Assert.That(reader.ReadShort(), Is.EqualTo((short) SendOp.ResponseTimeSync));
        Assert.That(reader.ReadByte(), Is.EqualTo(0));
        reader.Skip(4 + 8 + 4 + 1);
        Assert.That(reader.ReadInt(), Is.EqualTo(77));
    }
}
