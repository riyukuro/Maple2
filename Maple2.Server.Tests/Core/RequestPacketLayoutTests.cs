using System;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Tests.Core;

// RequestHeartbeat: CNetworkService::OnHeartbeatRequest (networkservice.cpp:749) reads one int, the sequence it answers
// with. RequestLogin and RequestKey carry no body (OnRequestKey reads nothing).
public class RequestPacketLayoutTests {
    private static string Op(SendOp op) => Convert.ToHexString([(byte) (ushort) op, (byte) ((ushort) op >> 8)]);

    [Test]
    public void HeartbeatIsOneInt_AndLoginAndKeyAreEmpty() {
        string heartbeat = Convert.ToHexString(RequestPacket.Heartbeat().ToArray());
        Assert.That(heartbeat, Does.StartWith(Op(SendOp.RequestHeartbeat)));
        Assert.That(heartbeat, Has.Length.EqualTo(Op(SendOp.RequestHeartbeat).Length + 8));
        Assert.That(Convert.ToHexString(RequestPacket.Login().ToArray()), Is.EqualTo(Op(SendOp.RequestLogin)));
        Assert.That(Convert.ToHexString(RequestPacket.Key().ToArray()), Is.EqualTo(Op(SendOp.RequestKey)));
    }
}
