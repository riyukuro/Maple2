using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Core.Constants;
using Maple2.Server.Login.PacketHandlers;
using Maple2.Server.Login.Session;
using Maple2.Server.Tests.Game;

namespace Maple2.Server.Tests.Login;

// The return to the character list: ResponseKey is answered with MoveResult only; the client then sends
// ServerEnter, which lists the banners, the servers and the characters once.
public class ServerEnterTests {
    [Test]
    public void TheListsAreSentOnceOnServerEnter() {
        var test = new TestLoginSession();
        LoginSession session = test.Start(init: false);
        var machineId = Guid.NewGuid();

        new ResponseKeyHandler { World = test.World }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteLong(test.AccountId);
            writer.WriteLong(77);
            writer.WriteBytes(machineId.ToByteArray());
        }));

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(Convert.ToHexString(sent.Single()), Is.EqualTo("0D00" + "00"));
            Assert.That(test.World.Arrivals.Select(arrival => (arrival.AccountId, arrival.Token, arrival.MachineId)),
                Is.EqualTo(new[] { (test.AccountId, 77UL, machineId.ToString()) }));
            Assert.That(session.AccountId, Is.EqualTo(test.AccountId));
        });

        new ServerEnterHandler().Handle(session, TestGameSession.Packet(writer => writer.WriteInt(1)));

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] {
            SendOp.BannerList, SendOp.ServerList,
            SendOp.CharMaxCount, SendOp.CharacterList, SendOp.CharacterList, SendOp.CharacterList,
            SendOp.GameEvent,
        }));
        Assert.That(test.Errors, Is.Empty);
    }
}
